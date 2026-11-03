# Deployment

## Requirements

Linux x64, Python 3.10+, .NET SDK 10, SteamCMD, tmux, FFmpeg and the game's [system dependencies](https://techwiki.scpslgame.com/books/server-guides/page/1-how-to-create-a-dedicated-server).

Tested with SCP: SL 14.2.9 and LabAPI 1.1.7. SteamCMD installs the current public release; plugin versions and checksums are in [manifest.json](../deployment/manifest.json).

## Installation

Run from the repository directory. The installation directory must be outside the repository and writable by the server account.

Paravoz needs a mono 48 kHz Ogg Vorbis track. The track is not included. To convert one:

```sh
ffmpeg -i /path/to/track.wav -vn -ac 1 -ar 48000 -c:a libvorbis /path/to/paravoz.ogg
```

```sh
python3 deployment/install.py --root /srv/scpsl --music /path/to/paravoz.ogg
```

The installer downloads the game, plugins and assets, then builds the community plugins. It refuses to overwrite an existing instance. The supplied track is embedded in `Methadrenaline.Server.dll`.

| Option | Default | Purpose |
| --- | --- | --- |
| `--port` | `7777` | Game port |
| `--steamcmd` | `steamcmd` | Path to the SteamCMD executable |
| `--skip-server-download` | Off | Use a game installation already present in `/srv/scpsl/game` |

## Configuration

Settings are installed under `/srv/scpsl/state/SCP Secret Laboratory/`:

| Path | Settings |
| --- | --- |
| `config/7777/config_gameplay.txt` | Server name, address, contact email and gameplay |
| `config/7777/config_remoteadmin.txt` | Admin groups and account assignments; `Members` starts empty |
| `config/7777/config_localadmin.txt` | Logging, crash recovery and heartbeat |
| `LabAPI/configs/7777/` | Plugin settings and translations |

Replace `7777` with the port passed to the installer. Open that port in the firewall and forward it on the router if needed.

For Server Info, upload [server-info.txt](../configs/server-info.txt) to Pastebin and set `serverinfo_pastebin_id` in the installed gameplay config.

## Start and stop

```sh
cd /srv/scpsl
./serverctl start
./serverctl console
```

Accept Northwood's EULA in the console on the first launch. Detach with Ctrl+B, then D.

`./serverctl status` shows whether the instance is running. `./serverctl stop` shuts it down.

Logs are in `/srv/scpsl/logs`. Game and plugin data are in `/srv/scpsl/state`. The launcher sets `XDG_CONFIG_HOME` to this directory.

If startup stays at `Central server public key is not loaded. Waiting...`, check connectivity to Northwood's authentication service and restart the instance.

## Shelley voting

Voting is off by default. To enable it, edit the installed `LabAPI/configs/7777/METHADRENALINE/config.yml`:

```yaml
mode_voting_enabled: true
mode_voting_bind_address: 127.0.0.1
mode_voting_port: 8790
mode_voting_token_file: /srv/scpsl/voting.token
```

The token file must contain 32–256 ASCII token characters, belong to the server account and have permissions `400` or `600`, with no links.

Set the matching endpoint and token in [Shelley](https://github.com/METHADRENALINE/Shelley). If the bot runs on another machine, bind to the server's private IPv4 address and allow the bot to reach port `8790`.
