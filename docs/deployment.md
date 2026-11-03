# Deployment

The repository contains plugin sources, public configuration templates and a pinned dependency manifest. Everything generated while running the server belongs in a separate runtime directory.

| Directory | Contents |
| --- | --- |
| `src/` | Community plugins and maintained upstream forks |
| `tests/` | Synthetic gameplay and integration tests |
| `configs/game/` | Gameplay, LocalAdmin, Remote Admin and instance isolation |
| `configs/plugins/` | Settings and translations for every installed plugin |
| `configs/server-info.txt` | Public Server Info text |
| `deployment/` | Installer, dependency versions, SHA-256 checksums and server controls |
| `tools/RepositoryPolicy/` | Publication checks for the Git index and history |

## Requirements

Use a dedicated Linux x64 account with Python 3.10+, SteamCMD, .NET SDK 10, tmux, FFmpeg and the system libraries required by the game. The tested game is 14.2.9 with LabAPI 1.1.7, included in the game. EXILED is not needed.

Follow the [official dedicated server installation guide](https://techwiki.scpslgame.com/books/server-guides/page/1-how-to-create-a-dedicated-server) for SteamCMD and OS dependencies. The installer downloads app 996560 from Steam's public branch. SteamCMD serves the current release; `manifest.json` records the tested build, rather than promising that Steam will retain old versions. Review game updates before replacing a working installation.

Provide your own licensed music outside the checkout. The community's private music is not distributed. Convert a replacement to mono 48 kHz Vorbis:

```sh
ffmpeg -i /private/music.wav -vn -ac 1 -ar 48000 -c:a libvorbis /private/paravoz.ogg
```

## Install

From the checkout, with a new runtime directory outside it:

```sh
python3 deployment/install.py --root /srv/scpsl --music /private/paravoz.ogg
```

Use `--steamcmd /path/to/steamcmd.sh` if SteamCMD is not in PATH. Use `--port 7778` for another instance. If SteamCMD has already installed a clean dedicated server into `/srv/scpsl/game`, add `--skip-server-download`. The installer refuses existing instance configuration. It never imports a running server, logs or player databases.

Upstream plugins and spell schematics/sounds are downloaded from pinned release URLs and checked against SHA-256. Our three projects are built from source. Generated DLLs, the supplied music and build output remain local; do not publish compiled community DLLs because they contain the supplied music.

## Private setup and start

Installed settings live under `/srv/scpsl/state/SCP Secret Laboratory`. The launcher sets a private `XDG_CONFIG_HOME` for the whole process tree. `hoster_policy.txt` keeps native path overrides off, so the game, LabAPI and third-party plugins all use the same isolated directory.

1. Edit `config/7777/config_gameplay.txt` for this machine's public address, contact details and network setup. The template uses automatic address detection, port 7777, 255 slots and no whitelist. Forward the chosen game port and configure the host firewall as needed.
2. Add your own account assignments to `config/7777/config_remoteadmin.txt` locally. The public template defines an owner group with no assigned members. Never copy the resulting file back into Git.
3. If desired, publish `configs/server-info.txt` to your own Pastebin and set `serverinfo_pastebin_id` in the installed gameplay configuration.
4. Start and inspect the instance:

```sh
cd /srv/scpsl
./serverctl start
./serverctl status
./serverctl console
```

On the first launch, open the console, read Northwood's EULA and answer its acceptance prompt to continue. Detach from tmux with Ctrl+B, then D. Stop with `./serverctl stop`. The installer does not start the server or modify another instance.

A fresh online instance must download Northwood's signed central public key. No authentication cache is copied from the community server. If startup stays at `Central server public key is not loaded. Waiting...`, check access to Northwood's authentication service and retry; keep `online_mode: true` for real players. Offline NPC diagnostics do not test Steam authentication.

## Optional Shelley voting

Standalone mode works without Discord. Voting is disabled in the public template because an installation needs its own private connection settings and shared token. Shelley's implementation remains in the [Shelley repository](https://github.com/METHADRENALINE/Shelley).

In the installed `LabAPI/configs/7777/METHADRENALINE/config.yml`, enable `mode_voting_enabled`, select a private IPv4 bind address and port, and set `mode_voting_token_file` to an absolute external token-file path. The token must contain 32–256 ASCII characters, belong to the server account, have mode 400 or 600, and have no links. Generate a fresh token privately; never reuse or publish an existing one. Configure Shelley's matching endpoint and token using its private settings. Restrict the API port to the bot host or VPN and keep it off the public Internet.

The authenticated API exposes `/status` and `/mode`; status responses contain online-player information and must never be committed. No Discord server/channel IDs, endpoint addresses, account exceptions or bot credentials are included here.

## Publication rules

Only reviewed templates may be edited in Git. Keep deployed configurations, account and badge assignments, nicknames, IDs, IP histories, bans, whitelist/reserved-slot lists, SCP tickets, preferences, votes, API responses, logs, metrics, caches, databases, backups, credentials and music outside this checkout.

The allowlist rejects unreviewed files even if force-added. Publication checks also validate sensitive configuration fields and scan identifiers, network addresses and credentials throughout Git history and the index. These checks supplement human review; they cannot recognize every possible nickname in arbitrary prose. Never export a live directory into the repository.

Before publishing:

```sh
python3 -m unittest discover -s tests/RepositoryPolicy -p 'test_*.py'
python3 -m unittest discover -s tests/Deployment -p 'test_*.py'
python3 tools/RepositoryPolicy/check.py --worktree
dotnet test tests/Methadrenaline.Server.Tests/Methadrenaline.Server.Tests.csproj -c Release
git add <reviewed-files>
python3 tools/RepositoryPolicy/check.py
git diff --cached
```

Use a Pull Request and wait for successful repository checks. Do not push directly to main.
