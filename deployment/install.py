#!/usr/bin/env python3
"""Install reviewed public templates and pinned plugins into a separate Linux runtime."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import platform
import shutil
import stat
import subprocess
import sys
import urllib.request
import zipfile

REPO = Path(__file__).resolve().parents[1]


def inside(root: Path, relative: str) -> Path:
    name = PurePosixPath(relative)
    if name.is_absolute() or '..' in name.parts or '\\' in relative:
        raise ValueError('Unsafe installation path')
    target = root.joinpath(*name.parts).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError('Installation path escapes its root')
    return target


def write(path: Path, data: bytes, mode: int = 0o600) -> None:
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    with path.open('xb') as stream:
        stream.write(data)
    path.chmod(mode)


def fetch(url: str, expected: str, cache: dict[str, bytes]) -> bytes:
    if not url.startswith(('https://github.com/', 'https://raw.githubusercontent.com/')):
        raise ValueError('Downloads must use a reviewed upstream HTTPS URL')
    if url not in cache:
        request = urllib.request.Request(url, headers={'User-Agent': 'SCP-SL-deployment'})
        with urllib.request.urlopen(request, timeout=90) as response:
            data = response.read(64 * 1024 * 1024 + 1)
        if len(data) > 64 * 1024 * 1024:
            raise ValueError('Download exceeds the size limit')
        cache[url] = data
    data = cache[url]
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError('Upstream download checksum mismatch')
    return data


def install_artifact(app: Path, entry: dict, port: int, cache: dict) -> None:
    data = fetch(entry['url'], entry.get('archive_sha256', entry['sha256']), cache)
    target = inside(app, entry['target'].replace('{port}', str(port)))
    if 'member' in entry:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            data = archive.read(entry['member'])
        if hashlib.sha256(data).hexdigest() != entry['sha256']:
            raise ValueError('Archive member checksum mismatch')
    if entry.get('extract'):
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            for member in archive.infolist():
                if member.is_dir():
                    continue
                if stat.S_ISLNK(member.external_attr >> 16) or member.file_size > 16 * 1024 * 1024:
                    raise ValueError('Unsafe schematic archive member')
                write(inside(target, member.filename), archive.read(member))
    else:
        write(target, data)


def copy_templates(app: Path, game: Path, port: int) -> None:
    for source in sorted((REPO / 'configs/game').glob('config_*.txt')):
        text = source.read_text(encoding='utf-8')
        if source.name == 'config_gameplay.txt':
            import re
            text = re.sub(r'(?m)^port_queue:\n(?:[ \t]+- [^\n]+\n)+', f'port_queue:\n - {port}\n', text)
        write(app / 'config' / str(port) / source.name, text.encode())
    for source in sorted((REPO / 'configs/plugins').rglob('*.yml')):
        write(app / 'LabAPI/configs' / str(port) / source.relative_to(REPO / 'configs/plugins'), source.read_bytes())
    write(game / 'hoster_policy.txt', (REPO / 'configs/game/hoster_policy.txt').read_bytes())


def build(game: Path, app: Path, port: int, music: Path) -> None:
    options = [f'-p:ServerManagedPath={game / "SCPSL_Data/Managed"}',
               f'-p:ServerDependenciesPath={app / "LabAPI/dependencies" / str(port)}',
               f'-p:ServerPluginsPath={app / "LabAPI/plugins" / str(port)}',
               f'-p:ParavozMusicFile={music}']
    projects = [('src/SecretAPI/SecretAPI/SecretAPI.csproj', ['SecretAPI.dll']),
                ('src/ScpProximityChat/ScpProximityChat.SecretAPI/ScpProximityChat.SecretAPI.csproj',
                 ['ScpProximityChat.Core.dll', 'ScpProximityChat.SecretAPI.dll']),
                ('src/Methadrenaline.Server/Methadrenaline.Server.csproj', ['Methadrenaline.Server.dll'])]
    for relative, names in projects:
        project = REPO / relative
        subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo', *options], check=True)
        for name in names:
            write(app / 'LabAPI/plugins' / str(port) / name, (project.parent / 'bin/Release/net48' / name).read_bytes())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True, help='New runtime directory outside the checkout')
    parser.add_argument('--music', type=Path, required=True, help='External, licensed 48000 Hz mono Ogg Vorbis file')
    parser.add_argument('--port', type=int, default=7777)
    parser.add_argument('--steamcmd', default='steamcmd')
    parser.add_argument('--skip-server-download', action='store_true', help='Use a clean game/ installed with SteamCMD')
    args = parser.parse_args()
    root = args.root.expanduser().resolve()
    music = args.music.expanduser().resolve()
    if root == Path(root.anchor) or root.is_relative_to(REPO) or REPO.is_relative_to(root):
        parser.error('Runtime must be separate from the repository')
    if platform.system() != 'Linux' or platform.machine() not in {'x86_64', 'AMD64'}:
        parser.error('This installer targets Linux x64')
    if not 1024 <= args.port <= 65535:
        parser.error('Use an unprivileged port between 1024 and 65535')
    if not music.is_file() or music.is_relative_to(REPO):
        parser.error('Provide a private music file outside the checkout')
    for command in ['dotnet', 'tmux', 'ffprobe']:
        if shutil.which(command) is None:
            parser.error(f'Required command is not available: {command}')
    probe = subprocess.run(['ffprobe', '-v', 'error', '-show_entries', 'stream=codec_name,sample_rate,channels',
                            '-of', 'json', str(music)], capture_output=True, text=True, check=True)
    streams = json.loads(probe.stdout).get('streams', [])
    if len(streams) != 1 or streams[0] != {'codec_name': 'vorbis', 'sample_rate': '48000', 'channels': 1}:
        parser.error('Music must be a single 48000 Hz mono Vorbis stream')
    game = root / 'game'
    app = root / 'state/SCP Secret Laboratory'
    if app.exists() or (game / 'hoster_policy.txt').exists() or (root / 'instance.json').exists():
        parser.error('Existing runtime detected; install into a new directory to preserve private data')
    os.umask(0o077)
    root.mkdir(parents=True, exist_ok=True, mode=0o700)
    manifest = json.loads((REPO / 'deployment/manifest.json').read_text())
    if not args.skip_server_download:
        subprocess.run([args.steamcmd, '+force_install_dir', str(game), '+login', 'anonymous',
                        '+app_update', str(manifest['game']['steam_app']), 'validate', '+quit'], check=True)
    managed = game / 'SCPSL_Data/Managed'
    for name in ['Assembly-CSharp.dll', 'LabApi.dll', 'mscorlib.dll']:
        if not (managed / name).is_file():
            parser.error('The dedicated game server is missing or incomplete')
    if not (game / 'LocalAdmin').is_file():
        parser.error('LocalAdmin is missing from the dedicated server')
    if not (game / 'ConfigTemplates/config_gameplay.template.txt').is_file():
        parser.error('Stock game ConfigTemplates are missing; validate the SteamCMD installation')
    copy_templates(app, game, args.port)
    cache: dict[str, bytes] = {}
    for entry in manifest['artifacts']:
        install_artifact(app, entry, args.port, cache)
    build(game, app, args.port, music)
    write(root / 'serverctl', (REPO / 'deployment/serverctl').read_bytes(), 0o700)
    write(root / 'instance.json', json.dumps({'port': args.port}, indent=2).encode() + b'\n')
    print('Installation complete. Edit private settings under state/SCP Secret Laboratory before starting ./serverctl start.')
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, subprocess.CalledProcessError, zipfile.BadZipFile) as error:
        print(f'Installation failed: {type(error).__name__}. No existing runtime was replaced.', file=sys.stderr)
        sys.exit(1)
