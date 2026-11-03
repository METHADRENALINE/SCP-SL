"""Reject private data, secrets and unreviewed files in Git history and the index."""

import argparse
import ipaddress
import re
import subprocess
import sys
from pathlib import Path, PurePosixPath

CONFIG_FILES = {
    'configs/game/config_gameplay.txt', 'configs/game/config_localadmin.txt',
    'configs/game/config_remoteadmin.txt', 'configs/game/config_sharing.txt',
    'configs/game/hoster_policy.txt', 'configs/server-info.txt',
    'configs/plugins/InventoryControl/config.yml', 'configs/plugins/InventoryControl/properties.yml',
    'configs/plugins/METHADRENALINE/config.yml', 'configs/plugins/METHADRENALINE/properties.yml',
    'configs/plugins/ProjectMER/config.yml', 'configs/plugins/ProjectMER/properties.yml',
    'configs/plugins/RueI/properties.yml',
    'configs/plugins/SCP Proximity Chat/config.yml', 'configs/plugins/SCP Proximity Chat/properties.yml',
    'configs/plugins/SCPSL Arcade Mode/config.yml', 'configs/plugins/SCPSL Arcade Mode/properties.yml',
    'configs/plugins/SSSS Proximity Chat/config.yml', 'configs/plugins/SSSS Proximity Chat/properties.yml',
    'configs/plugins/SSSS Proximity Chat/translations.yml', 'configs/plugins/SecretAPI/properties.yml',
    'configs/plugins/SecretLabNAudio/properties.yml', 'configs/plugins/SecretLabNAudio.FFmpeg/config.yml',
    'configs/plugins/SecretLabNAudio.FFmpeg/properties.yml',
    'configs/plugins/Talky/config.yml', 'configs/plugins/Talky/properties.yml',
    'configs/plugins/UltimateHUD/config.yml', 'configs/plugins/UltimateHUD/properties.yml',
    'configs/plugins/UltimateHUD/translation.yml',
}
DEPLOYMENT_FILES = {
    'deployment/install.py', 'deployment/serverctl', 'deployment/manifest.json',
    'docs/deployment.md', '.gitattributes',
    'tests/RepositoryPolicy/test_policy.py', 'tests/Deployment/test_install.py',
}


def git(*args: str) -> bytes:
    return subprocess.check_output(['git', *args])


def allowed(path: str) -> bool:
    p = PurePosixPath(path)
    if p.is_absolute() or '..' in p.parts or '\\' in path:
        return False
    if any(part.lower() in {'bin', 'obj', 'testresults', 'upstream', 'references', 'logs', 'data', 'backups', 'players'} for part in p.parts):
        return False
    if p.name.lower().startswith('readme'):
        return False
    if path in CONFIG_FILES | DEPLOYMENT_FILES:
        return True
    if path in {'.gitignore', '.github/workflows/verify.yml', 'LICENSE', 'tools/RepositoryPolicy/check.py',
                'tools/LocalizationCatalog/Program.cs', 'tools/LocalizationCatalog/LocalizationCatalog.csproj',
                'src/Methadrenaline.Server/Localization/catalog.json'}:
        return True
    if path in {'src/SecretAPI/LICENSE', 'src/ScpProximityChat/LICENSE',
                'src/SecretAPI/SecretAPI.SourceGenerators/AnalyzerReleases.Shipped.md',
                'src/SecretAPI/SecretAPI.SourceGenerators/AnalyzerReleases.Unshipped.md'}:
        return True
    return (p.parts[0] == 'src' and p.suffix in {'.cs', '.csproj', '.props', '.targets', '.slnx'} or
            p.parts[0] == 'tests' and p.suffix in {'.cs', '.csproj'})


PATTERNS = {
    'private filesystem path': re.compile(r'[/]home[/][^/\s]+/|[A-Za-z]:[\\/]Users[\\/]', re.I),
    'Steam account identifier': re.compile(r'\b765611\d{11}\b'),
    'credential': re.compile(r'(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |OPENSSH |EC )?PRIVATE KEY-----|mfa\.[A-Za-z0-9_-]{20,})'),
    'email address': re.compile(r'\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b'),
}
IPV4 = re.compile(r'(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])')
# Generic addresses used only for loopback operation and synthetic policy tests.
EXAMPLE_IPS = {'0.0.0.0', '127.0.0.1', '10.0.0.1', '10.0.0.2', '172.16.0.1', '192.168.1.10', '8.8.8.8'}
EXAMPLE_IDS = {'123456789012345678', '223456789012345678', '18446744073709551616'}


def problems(path: str, data: bytes) -> list[str]:
    if not allowed(path):
        return ['file is outside the reviewed source and template allowlist']
    try:
        text = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return ['binary files are prohibited']
    if '\0' in text:
        return ['binary content is prohibited']
    found = [name for name, pattern in PATTERNS.items() if pattern.search(text)]
    found.extend(template_problems(path, text))
    numeric_ids = re.findall(r'(?<![\w])\d{17,20}(?![\w])', text)
    if any(value not in EXAMPLE_IDS for value in numeric_ids):
        found.append('non-example account identifier')
    # Only JSON version fields are version metadata; network fields still get scanned.
    address_text = re.sub(r'"version":\s*"[0-9]+(?:\.[0-9]+){3}"', '', text) if path == 'deployment/manifest.json' else text
    for candidate in IPV4.findall(address_text):
        try:
            address = ipaddress.ip_address(candidate)
        except ValueError:
            continue
        if candidate not in EXAMPLE_IPS and not address.is_loopback and not any(address in ipaddress.ip_network(net) for net in ('192.0.2.0/24', '198.51.100.0/24', '203.0.113.0/24')):
            # Version strings in build metadata are not network addresses.
            if path.endswith(('.props', '.csproj')):
                continue
            found.append('non-example network address')
            break
    return found


def value(text: str, key: str) -> str | None:
    matches = re.findall(r'^' + re.escape(key) + r':[^\S\n]*(.*)$', text, re.M)
    return matches[0].strip() if len(matches) == 1 else None


def template_problems(path: str, text: str) -> list[str]:
    """These fields may only contain public defaults, even without recognizable IDs."""
    expected = {}
    if path == 'configs/game/config_gameplay.txt':
        expected = {'server_ip': 'auto', 'contact_email': 'default', 'query_administrator_password': 'none',
                    'query_permissions': '0', 'serverinfo_pastebin_id': 'default',
                    'ff_detector_webhook_url': 'none', 'report_send_using_discord_webhook': 'false',
                    'report_discord_webhook_url': 'PleaseSetWebhookUrlHere'}
    elif path == 'configs/game/config_remoteadmin.txt':
        expected = {'Members': '', 'override_password': 'none', 'owner_badge': 'Owner'}
        members = re.search(r'^Members:[^\n]*\n((?:(?:[ \t]+[^\n]*|#[^\n]*|)\n)*)', text, re.M)
        if not members or any(line.strip() and not line.lstrip().startswith('#') for line in members.group(1).splitlines()):
            return ['administrator assignments must be empty']
        if re.search(r'^(?!owner_)[\w]+_(?:badge|color|cover|hidden|kick_power|required_kick_power):', text, re.M):
            return ['personalized administrator groups are prohibited']
    elif path == 'configs/plugins/METHADRENALINE/config.yml':
        expected = {'basic_asn_exempt_user_ids': '[]', 'mode_voting_enabled': 'false',
                    'mode_voting_bind_address': '127.0.0.1', 'mode_voting_token_file': "''"}
    elif path == 'configs/plugins/InventoryControl/config.yml':
        expected = {'inventory_rank': '{}'}
    elif path == 'configs/game/hoster_policy.txt':
        expected = {'gamedir_for_configs': 'false'}
    issues = [f'private or missing template field: {key}' for key, wanted in expected.items() if value(text, key) != wanted]
    for key, wanted in expected.items():
        if wanted not in {'[]', '{}'}:
            continue
        block = re.search(r'^' + re.escape(key) + r':[^\n]*\n((?:(?:[ \t]+[^\n]*|#[^\n]*|)\n)*)', text, re.M)
        if block and any(line.strip() and not line.lstrip().startswith('#') for line in block.group(1).splitlines()):
            issues.append(f'private template collection must be empty: {key}')
    return issues


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worktree', action='store_true', help='Also inspect current tracked and unignored files')
    args = parser.parse_args()
    revisions = git('rev-list', '--all').decode().splitlines()
    entries = []
    for revision in revisions:
        entries.extend(git('ls-tree', '-r', '-z', revision).split(b'\0'))
    entries.extend(git('ls-files', '--stage', '-z').split(b'\0'))
    checked = set()
    failures = []
    for entry in entries:
        if not entry:
            continue
        metadata, raw_path = entry.split(b'\t', 1)
        mode, _, oid = metadata.split() if b' blob ' in metadata else (metadata.split()[0], b'blob', metadata.split()[1])
        path = raw_path.decode('utf-8')
        if (path, oid) in checked:
            continue
        checked.add((path, oid))
        issues = ['symlinks and submodules are prohibited'] if mode not in (b'100644', b'100755') else problems(path, git('cat-file', 'blob', oid.decode()))
        failures.extend(f'{path}: {issue}' for issue in issues)
    if args.worktree:
        for raw_path in git('ls-files', '--cached', '--others', '--exclude-standard', '-z').split(b'\0'):
            if not raw_path:
                continue
            path = raw_path.decode('utf-8')
            file = Path(path)
            if not file.exists():
                continue
            issues = ['symlinks are prohibited'] if file.is_symlink() else problems(path, file.read_bytes())
            failures.extend(f'{path}: {issue}' for issue in issues)
    if failures:
        print('\n'.join(sorted(set(failures))))
        return 1
    print(f'Source policy passed for {len(checked)} file versions across Git history and the index.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
