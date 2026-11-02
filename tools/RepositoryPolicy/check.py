"""Reject runtime data, private identifiers, secrets and media in Git history."""

import ipaddress
import re
import subprocess
import sys
from pathlib import PurePosixPath


def git(*args: str) -> bytes:
    return subprocess.check_output(['git', *args])


def allowed(path: str) -> bool:
    p = PurePosixPath(path)
    if any(part.lower() in {'bin', 'obj', 'testresults', 'upstream', 'references', 'logs', 'data', 'backups', 'players'} for part in p.parts):
        return False
    if p.name.lower().startswith('readme'):
        return False
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
        return ['file is outside the source-only allowlist']
    try:
        text = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        return ['binary files are prohibited']
    if '\0' in text:
        return ['binary content is prohibited']
    found = [name for name, pattern in PATTERNS.items() if pattern.search(text)]
    numeric_ids = re.findall(r'(?<![\w])\d{17,20}(?![\w])', text)
    if any(value not in EXAMPLE_IDS for value in numeric_ids):
        found.append('non-example account identifier')
    for candidate in IPV4.findall(text):
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


def main() -> int:
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
    if failures:
        print('\n'.join(sorted(set(failures))))
        return 1
    print(f'Source policy passed for {len(checked)} file versions across Git history and the index.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
