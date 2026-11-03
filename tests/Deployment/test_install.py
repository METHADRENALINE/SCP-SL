import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('install', ROOT / 'deployment/install.py')
install = importlib.util.module_from_spec(spec)
spec.loader.exec_module(install)


class DeploymentTests(unittest.TestCase):
    def test_manifest_has_pinned_downloads_and_no_runtime_data(self):
        manifest = json.loads((ROOT / 'deployment/manifest.json').read_text())
        targets = set()
        for entry in manifest['artifacts']:
            self.assertRegex(entry['sha256'], r'^[a-f0-9]{64}$')
            self.assertTrue(entry['url'].startswith(('https://github.com/', 'https://raw.githubusercontent.com/')))
            self.assertNotIn(entry['target'], targets)
            targets.add(entry['target'])
            if 'member' in entry:
                self.assertRegex(entry['archive_sha256'], r'^[a-f0-9]{64}$')
        self.assertIn('LabAPI/dependencies/{port}/AudioPlayerApi.dll', targets)
        self.assertIn('LabAPI/plugins/global/SecretLabNAudio.dll', targets)
        self.assertIn('LabAPI/dependencies/{port}/0Harmony.dll', targets)
        self.assertFalse(any('paravoz-track' in t for t in targets))

    def test_templates_go_to_an_isolated_instance(self):
        with tempfile.TemporaryDirectory() as directory:
            game = Path(directory) / 'game'
            app = Path(directory) / 'state/SCP Secret Laboratory'
            install.copy_templates(app, game, 7788)
            files = [p for p in Path(directory).rglob('*') if p.is_file()]
            self.assertEqual(28, len(files))
            text = (app / 'config/7788/config_gameplay.txt').read_text()
            self.assertIn('port_queue:\n - 7788\n', text)
            self.assertNotIn(' - 7777\n', text)
            self.assertIn('Members:\n\n', (app / 'config/7788/config_remoteadmin.txt').read_text())
            self.assertTrue((app / 'LabAPI/configs/7788/METHADRENALINE/config.yml').is_file())
            self.assertEqual({'config', 'LabAPI'}, {p.name for p in app.iterdir()})

    def test_existing_files_are_preserved(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / 'private.txt'
            target.write_bytes(b'keep this')
            with self.assertRaises(FileExistsError):
                install.write(target, b'replacement')
            self.assertEqual(b'keep this', target.read_bytes())

    def test_download_checksum_is_required_even_for_cached_content(self):
        url = 'https://github.com/example/project/release'
        with self.assertRaises(ValueError):
            install.fetch(url, '0' * 64, {url: b'changed download'})

    def test_archive_cannot_escape_runtime(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            archive.writestr('../../private.txt', b'bad')
        data = stream.getvalue()
        entry = {'url': 'https://github.com/example/project/release', 'target': 'schematics',
                 'sha256': hashlib.sha256(data).hexdigest(), 'extract': True}
        with tempfile.TemporaryDirectory() as directory, patch.object(install, 'fetch', return_value=data):
            with self.assertRaises(ValueError):
                install.install_artifact(Path(directory), entry, 7777, {})

    def test_archive_member_checksum_is_required(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, 'w') as archive:
            archive.writestr('plugin.dll', b'wrong')
        entry = {'url': 'https://github.com/example/project/release', 'target': 'plugin.dll',
                 'member': 'plugin.dll', 'sha256': '0' * 64, 'archive_sha256': '1' * 64}
        with tempfile.TemporaryDirectory() as directory, patch.object(install, 'fetch', return_value=stream.getvalue()):
            with self.assertRaises(ValueError):
                install.install_artifact(Path(directory), entry, 7777, {})


if __name__ == '__main__':
    unittest.main()
