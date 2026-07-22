# HappyFinger

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/JoyfulReaper/HappyFinger)](LICENSE)
[![GitHub Repo](https://img.shields.io/badge/GitHub-JoyfulReaper%2FHappyFinger-181717?logo=github)](https://github.com/JoyfulReaper/HappyFinger)

A tiny Finger protocol server written in C# and .NET 10.

HappyFinger listens on TCP port `79`, reads a Finger request, and writes back a simple text response. It is intentionally small, plain text, and a little ridiculous.

No web framework. No database. No JavaScript. Just a TCP listener and an old internet protocol that somehow still makes people smile.

## Status

Early experimental project.

Current behavior:

* Runs as a .NET Worker service
* Listens on a configurable address and port
* Defaults to TCP port `79`, the classic Finger port
* Limits concurrent connections
* Enforces a request timeout
* Reads a single request line ending in `\n` or `\r\n`
* Responds with file-backed directory and profile-style text records
* Ships packaged default records and can read optional production overrides
* Can run from the console during development
* Includes Windows Service support through .NET hosting

The packaged default responses are intentionally simple records such as:

```text
HappyFinger Public Directory

Login         Description
------------  ------------------------------------------
kyle          About Kyle Givler
now           What Kyle is currently working on
projects      Current software projects
services      Public services running on this server
```

Future versions may serve profile text, project status, GitHub activity, uptime, or other small public status information.

# Try it live:
finger.kgivler.com:79

## Requirements

* [.NET 10 SDK](https://dotnet.microsoft.com/download)
* Linux, Windows, or any platform supported by .NET 10
* TCP port `79` open if you want to expose it publicly

## Getting Started

Clone the repository:

```powershell
git clone https://github.com/JoyfulReaper/HappyFinger.git
cd HappyFinger
```

Build it:

```powershell
dotnet build HappyFinger.slnx
```

Run it locally:

```powershell
dotnet run --project .\HappyFinger\HappyFinger.csproj
```

The default checked-in configuration listens on loopback only:

```text
127.0.0.1:79
```

With this default, HappyFinger accepts local connections only. To serve other machines, change `ListenAddress` to `0.0.0.0` and review your firewall rules.

## Configuration

Configuration lives in:

```text
HappyFinger/appsettings.json
```

The default configuration resembles:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Finger": {
    "ListenAddress": "127.0.0.1",
    "Port": 79,
    "MaxConcurrentConnections": 64,
    "RequestTimeoutSeconds": 15
  },
  "PlanFile": {
    "Path": "data/.plan",
    "MaxBytes": 16384
  },
  "FingerContent": {
    "OverrideDirectory": null,
    "MaxBytes": 16384
  },
  "RandomSteamGame": {
    "BaseUrl": "https://randomsteam.kgivler.com/",
    "TimeoutSeconds": 5
  }
}
```

### Configuration Options

| Setting                           |     Default | Description                                       |
| --------------------------------- | ----------: | ------------------------------------------------- |
| `ListenAddress`                   | `127.0.0.1` | Local IP address on which the TCP server listens. |
| `Port`                            |        `79` | TCP port used by the server.                      |
| `MaxConcurrentConnections`        |        `64` | Maximum number of active client connections.      |
| `RequestTimeoutSeconds`           |        `15` | Time allowed for a client to send a request line. |
| `PlanFile:Path`                   | `data/.plan` | Trusted path for the `now` record `.plan` file.  |
| `PlanFile:MaxBytes`               |     `16384` | Maximum `.plan` bytes read per request.           |
| `FingerContent:OverrideDirectory` |      `null` | Optional absolute directory for editable record overrides. |
| `FingerContent:MaxBytes`          |     `16384` | Maximum static record bytes read per request.     |
| `RandomSteamGame:BaseUrl`         | `https://randomsteam.kgivler.com/` | Random Steam Game API base URL. |
| `RandomSteamGame:TimeoutSeconds`  |         `5` | Timeout for Random Steam Game API calls.          |

For a public server, bind to all interfaces:

```json
"ListenAddress": "0.0.0.0"
```

## Testing Locally

If you have `nc` or `netcat` available:

```bash
printf "kyle\r\n" | nc 127.0.0.1 79
```

Expected response:

```text
Login: kyle
Name: Kyle Givler
Website: https://kgivler.com
```

Verbose Finger queries are accepted only in these forms:

```text
/W
/W <login>
```

`/Wrong`, `/Whatever`, and `/Wkyle` are ordinary query strings, not verbose
queries.

Pick a random game from a public Steam library with a 17-digit Steam ID:

```bash
finger 10000000000000000@finger.kgivler.com
```

The Steam profile and game details must be public. HappyFinger calls the
configured Random Steam Game API and each request may return a different game.
If Random Steam Game cannot select a usable game, HappyFinger returns a generic
file-backed unavailable message instead of exposing upstream error details.

## File-Backed Static Records

Static public records are loaded from trusted text files. Packaged defaults are
included in the application under `content/`, so HappyFinger can serve records
without any external files. Production can optionally set
`FingerContent:OverrideDirectory` to an absolute directory of editable `.txt`
files. Override files are read on each matching request, so changes take effect
on the next Finger request without a container restart.

The route set is fixed in code. Query text, usernames, Steam IDs, response
types, URLs, and request contents are never treated as file paths. Missing,
empty, whitespace-only, or unreadable override files fall back to the packaged
defaults. If both override and packaged files are unavailable, HappyFinger
returns a small emergency response using the intended controlled response type.

Static records are decoded as UTF-8, bounded by `FingerContent:MaxBytes`,
normalized to CRLF, and stripped of unsafe terminal control and Unicode
formatting characters. Tabs and meaningful internal blank lines are preserved.
If a static record exceeds the configured size, HappyFinger returns the
permitted content with `[Content truncated]`.

## Traditional `.plan` Support

HappyFinger maps the `now` record to a configured traditional `.plan` file.
Both of these requests read the same configured file:

```text
now
/W now
```

When the file is available, the response begins with `Kyle's Plan`, followed by
the file contents. If the file is missing, empty, unreadable, or otherwise
unavailable, HappyFinger safely falls back to the file-backed `now-fallback.txt`
record. The configured maximum read size prevents unbounded file reads; if the
file is larger than the limit, HappyFinger returns the permitted content and
adds `[Plan truncated]`.

The `.plan` file path comes only from trusted application configuration. Finger
queries are never interpreted as filenames. HappyFinger strips unsafe terminal
control and formatting characters, preserves tabs and line breaks, normalizes
line endings to CRLF, and never adds plan contents or the configured path to
Mission Control telemetry.

## Mission Control Telemetry

HappyFinger publishes one `happyfinger.request.completed` event for each handled
request, except application shutdown cases and the configured Uptime Kuma
monitoring address.

Payload fields:

| Field                  | Description                                                   |
| ---------------------- | ------------------------------------------------------------- |
| `requestReceived`      | Whether a Finger request was read before processing ended.    |
| `request`              | Sanitized submitted Finger query for private diagnostics.     |
| `requestLength`        | Length of the request line after trimming trailing newlines.  |
| `remote`               | Remote endpoint string for operational diagnostics.           |
| `responseType`         | Predefined response selected by HappyFinger.                  |
| `durationMilliseconds` | Time spent handling the connection.                           |
| `outcome`              | Controlled processing outcome such as `served` or `timeout`.  |
| `succeeded`            | Whether the request was successfully served.                  |

`responseType` is a controlled telemetry value, not raw user input. Allowed
values are:

```text
directory
kyle
now
projects
services
randomsteam
reapershell
help
forwarding-not-supported
not-found
joke
none
random-game
random-game-unavailable
```

`randomsteam` is the static project-information record. `random-game` means a
17-digit Steam ID query returned a game. `random-game-unavailable` means the
Steam ID was valid, but Random Steam Game could not return a usable game.

The `request` field includes the submitted Finger query after telemetry
sanitization. It removes carriage returns and line feeds, converts tabs to
spaces, removes unsafe control and Unicode formatting characters, trims leading
and trailing whitespace, and truncates values longer than 100 characters with
`...`. Queries may contain usernames, selectors, Steam IDs, forwarding requests,
malformed probes, or other client-supplied text.

The submitted query is intended for private operational diagnostics and usage
analysis. Operators should not expose raw telemetry publicly without appropriate
output escaping and consideration of privacy.

HappyFinger telemetry does not include selected game names, Steam app IDs, API
URLs, response bodies, `.plan` contents, static record contents, override file
paths, configured filesystem paths, or override usage. Random Steam Game
publishes its own detailed game-pick telemetry independently.

Example payload:

```json
{
  "requestReceived": true,
  "request": "kyle",
  "requestLength": 4,
  "remote": "203.0.113.10:54321",
  "responseType": "kyle",
  "durationMilliseconds": 3,
  "outcome": "served",
  "succeeded": true
}
```

From PowerShell, you can test with raw TCP:

```powershell
$client = [System.Net.Sockets.TcpClient]::new("127.0.0.1", 79)
$stream = $client.GetStream()

$writer = [System.IO.StreamWriter]::new($stream)
$writer.NewLine = "`r`n"
$writer.AutoFlush = $true
$writer.WriteLine("kyle")

$reader = [System.IO.StreamReader]::new($stream)
$reader.ReadToEnd()

$client.Dispose()
```

If you have a Finger client installed:

```bash
finger kyle@127.0.0.1
```

## Publishing For Linux

Publish a self-contained Linux executable:

```powershell
dotnet publish .\HappyFinger\HappyFinger.csproj `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output .\publish\happyfinger
```

The publish output should include the executable, `appsettings.json`, and the
packaged `content/` directory.

## VPS Deployment Walkthrough

These notes assume an Ubuntu or Debian-style VPS, a deploy user with sudo access, and an install directory of `/opt/happyfinger`.

### 1. Copy The Publish Output

From your local machine:

```powershell
scp -r .\publish\happyfinger\* youruser@YOUR_VPS_IP:/tmp/happyfinger/
```

SSH into the VPS:

```powershell
ssh youruser@YOUR_VPS_IP
```

Create the install directory and copy the files:

```bash
sudo mkdir -p /opt/happyfinger
sudo cp -r /tmp/happyfinger/* /opt/happyfinger/
sudo chmod +x /opt/happyfinger/HappyFinger
```

If your executable has a different name, check the directory:

```bash
ls -la /opt/happyfinger
```

### 2. Create A Service User

```bash
sudo useradd \
  --system \
  --no-create-home \
  --shell /usr/sbin/nologin \
  happyfinger
```

Give the service user ownership of the app files:

```bash
sudo chown -R happyfinger:happyfinger /opt/happyfinger
```

### 3. Configure The App

Edit the VPS copy of `appsettings.json`:

```bash
sudo nano /opt/happyfinger/appsettings.json
```

For a public Finger server, use:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Finger": {
    "ListenAddress": "0.0.0.0",
    "Port": 79,
    "MaxConcurrentConnections": 64,
    "RequestTimeoutSeconds": 15
  }
}
```

### 4. Create A systemd Service

Create the service file:

```bash
sudo nano /etc/systemd/system/happyfinger.service
```

Paste:

```ini
[Unit]
Description=HappyFinger Finger Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=happyfinger
Group=happyfinger
WorkingDirectory=/opt/happyfinger
ExecStart=/opt/happyfinger/HappyFinger
Restart=always
RestartSec=5

# Allows a non-root process to bind TCP port 79.
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

# Basic hardening.
PrivateTmp=true
ProtectHome=true

[Install]
WantedBy=multi-user.target
```

If the published executable has a different name, update `ExecStart`.

### 5. Start The Service

```bash
sudo systemctl daemon-reload
sudo systemctl enable happyfinger
sudo systemctl start happyfinger
sudo systemctl status happyfinger --no-pager
```

View logs:

```bash
sudo journalctl -u happyfinger -n 100 --no-pager
```

Follow logs live:

```bash
sudo journalctl -u happyfinger -f
```

### 6. Open The Firewall

If using `ufw`:

```bash
sudo ufw allow 79/tcp
sudo ufw status
```

Also check your VPS provider firewall or security group. Many providers block inbound ports until they are allowed in the provider dashboard.

### 7. DNS Notes

If using Cloudflare DNS, set the Finger hostname record to **DNS only**, not proxied.

Finger uses raw TCP port `79`. Cloudflare's normal orange-cloud HTTP proxy will not proxy arbitrary TCP Finger traffic.

Example DNS record:

```text
finger.example.com  A  YOUR_VPS_IP  DNS only
```

### 8. Test From The VPS

```bash
printf "kyle\r\n" | nc 127.0.0.1 79
```

### 9. Test From Another Machine

```bash
finger kyle@finger.example.com
```

Or with PowerShell raw TCP:

```powershell
$client = [System.Net.Sockets.TcpClient]::new("finger.example.com", 79)
$stream = $client.GetStream()

$writer = [System.IO.StreamWriter]::new($stream)
$writer.NewLine = "`r`n"
$writer.AutoFlush = $true
$writer.WriteLine("kyle")

$reader = [System.IO.StreamReader]::new($stream)
$reader.ReadToEnd()

$client.Dispose()
```

## Updating A VPS Deployment

Republish locally:

```powershell
dotnet publish .\HappyFinger\HappyFinger.csproj `
    --configuration Release `
    --runtime linux-x64 `
    --self-contained true `
    --output .\publish\happyfinger
```

Copy to the VPS:

```powershell
scp -r .\publish\happyfinger\* youruser@YOUR_VPS_IP:/tmp/happyfinger/
```

On the VPS:

```bash
sudo systemctl stop happyfinger
sudo cp -r /tmp/happyfinger/* /opt/happyfinger/
sudo chown -R happyfinger:happyfinger /opt/happyfinger
sudo chmod +x /opt/happyfinger/HappyFinger
sudo systemctl start happyfinger
sudo systemctl status happyfinger --no-pager
```

### Docker Compose Data Directory Mount

For Compose deployments, mount the whole HappyFinger data directory read-only.
The `.plan` file remains separate from editable static records:

```yaml
services:
  happyfinger:
    environment:
      PlanFile__Path: "/data/happyfinger/.plan"
      PlanFile__MaxBytes: "16384"

      FingerContent__OverrideDirectory: "/data/happyfinger/records"
      FingerContent__MaxBytes: "16384"

      RandomSteamGame__BaseUrl: "https://randomsteam.kgivler.com/"
      RandomSteamGame__TimeoutSeconds: "5"

    volumes:
      - ./data/happyfinger:/data/happyfinger:ro
```

Remove the previous single-file mount when switching to the directory mount:

```yaml
- ./data/happyfinger/.plan:/data/.plan:ro
```

No additional Docker network or exposed container port is required for the
initial Random Steam integration because HappyFinger calls the public HTTPS
endpoint.

Create the host directories:

```bash
cd /opt/joyful-stack

mkdir -p data/happyfinger/records
```

Seed editable production records from a repository checkout:

```bash
cp HappyFinger/HappyFinger/content/*.txt \
  data/happyfinger/records/
```

Do not overwrite an existing production `.plan` during updates. Create it only
when needed:

```bash
cat > data/happyfinger/.plan <<'EOF'
Building HappyFinger into a useful public directory.
Next up: Random Steam Game integration.
EOF
```

Then recreate only the HappyFinger container:

```bash
docker compose up -d \
  --no-deps \
  --force-recreate \
  happyfinger
```

The container only reads the mounted file; it does not need write access.
Editing a mounted `.txt` record takes effect on the next matching Finger
request without recreating or restarting the container.

## Troubleshooting

Check service status:

```bash
sudo systemctl status happyfinger --no-pager
```

Check logs:

```bash
sudo journalctl -u happyfinger -n 100 --no-pager
```

Check whether something is listening on port `79`:

```bash
sudo ss -tulpn | grep :79
```

Common problems:

* Port `79` is blocked by the VPS firewall.
* Port `79` is blocked by the VPS provider firewall.
* The DNS record is proxied through Cloudflare instead of DNS-only.
* The app is still listening on `127.0.0.1` instead of `0.0.0.0`.
* The executable path in `ExecStart` is wrong.
* The executable is missing execute permission.
* The service user cannot read the app files.
* Low-port binding failed because `CAP_NET_BIND_SERVICE` is missing from the systemd unit.

## Current Limitations

* Fixed route set only
* No per-user profile lookup yet
* No dynamic project/status output yet
* No authentication or access control
* No TLS; Finger traffic is plaintext
* No official packaged releases yet

## Project Structure

```text
HappyFinger.slnx
├── HappyFinger/
│   ├── content/
│   ├── Program.cs
│   ├── FingerWorker.cs
│   ├── HappyFingerOptions.cs
│   ├── HappyFinger.csproj
│   └── appsettings.json
└── LICENSE
```

## Future Ideas

* Serve profile text from a file.
* Support multiple usernames.
* Add project/status output.
* Show latest GitHub activity.
* Show uptime or current project focus.
* Add a tiny admin reload signal.
* Add Linux systemd packaging notes or scripts.
* Add tests for request reading and timeout behavior.

## License

HappyFinger is available under the [MIT License](LICENSE).

Copyright © 2026 Kyle Givler.
