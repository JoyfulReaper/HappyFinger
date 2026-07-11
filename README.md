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
* Responds with a fixed text message
* Can run from the console during development
* Includes Windows Service support through .NET hosting

The current response is intentionally simple:

```text
You fingered me! How dare you!
```

Future versions may serve profile text, project status, GitHub activity, uptime, or other small public status information.

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
  }
}
```

### Configuration Options

| Setting                    |     Default | Description                                       |
| -------------------------- | ----------: | ------------------------------------------------- |
| `ListenAddress`            | `127.0.0.1` | Local IP address on which the TCP server listens. |
| `Port`                     |        `79` | TCP port used by the server.                      |
| `MaxConcurrentConnections` |        `64` | Maximum number of active client connections.      |
| `RequestTimeoutSeconds`    |        `15` | Time allowed for a client to send a request line. |

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
You fingered me! How dare you!
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

The publish output should include the executable and `appsettings.json`.

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

* Fixed response only
* No per-user profile lookup yet
* No dynamic project/status output yet
* No authentication or access control
* No TLS; Finger traffic is plaintext
* No official packaged releases yet

## Project Structure

```text
HappyFinger.slnx
├── HappyFinger/
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
