# TestServer

TestServer is a small ASP.NET Core HTTP API for querying server information and managing Linux power operations. New clients should use the versioned structured API. The original text-command endpoint remains available for compatibility:

```text
POST /api/message
```

Standard structured routes require `X-API-Key`. Structured restart and shutdown routes require only `X-Admin-API-Key`. Legacy restart and shutdown commands require both keys. Both power operations are disabled by default.

## Requirements

Server:

- .NET 10 SDK
- Linux for restart and shutdown operations

Client:

- Python 3.10 or newer
- No third-party Python packages

## Configuration

Do not store real keys in `appsettings.json`. Set them with environment variables:

```bash
export Authentication__ApiKey="replace-with-a-long-random-key"
export Authentication__AdminApiKey="replace-with-a-different-random-key"
```

You can generate keys on Linux with:

```bash
openssl rand -hex 32
openssl rand -hex 32
```

The standard API key is sent in `X-API-Key`. The administrator key is sent in `X-Admin-API-Key`. The structured API selects one key according to the route; the legacy command endpoint always checks the standard key first and additionally checks the administrator key for enabled power commands.

If the key required by a protected route is missing from server configuration, that route fails with HTTP `503`.

## Run locally

Set the keys, then run:

```bash
dotnet run
```

The local launch profile listens on:

```text
https://localhost:7020
http://localhost:5024
```

If the local HTTPS certificate is not trusted, initialize it with:

```bash
dotnet dev-certs https --trust
```

The trust option may not be supported on every Linux desktop. For local testing only, the Python client also provides `--insecure`.

## Versioned structured API

Use these routes for new integrations:

| Method and route | Purpose | Required header |
|---|---|---|
| `GET /api/v1/health/live` | Confirms that the API process is responding | `X-API-Key` |
| `GET /api/v1/health/ready` | Confirms that required keys are configured | `X-API-Key` |
| `GET /api/v1/server/info` | Returns basic server information | `X-API-Key` |
| `GET /api/v1/server/diagnostics` | Returns detailed diagnostics | `X-API-Key` |
| `POST /api/v1/power/restart` | Schedules a Linux restart | `X-Admin-API-Key` |
| `POST /api/v1/power/shutdown` | Schedules a Linux shutdown | `X-Admin-API-Key` |

Readiness requires the standard key configuration. It requires the administrator key configuration only when restart or shutdown is enabled.

Standard structured routes and the legacy endpoint allow 30 requests per minute per observed IP. Structured power routes have a separate limit of 5 requests per minute per observed IP.

## Legacy text commands

Commands are case-insensitive. Leading and trailing whitespace is removed. A message longer than 256 characters is rejected.

| Command | Result | Extra requirements |
|---|---|---|
| `Health status` | Returns `Status OK` | API key |
| `Ping` | Returns `Pong` | API key |
| `Server time` | Returns the current UTC time | API key |
| `Server info` | Returns basic host, memory, disk, uptime, and runtime information | API key |
| `Server diagnostics` | Returns detailed host, process, network, memory, and disk information | API key |
| `What is the item the cursor is pointing at?` | Reports that object recognition is unavailable because no image was provided | API key |
| `AI make this person pregnant` | Refuses the request and offers general question assistance | API key |
| `All available commands` | Returns the server's primary operational command list | API key |
| `Restart Linux machine` | Schedules a Linux reboot | API key, admin key, and restart enabled |
| `Shutdown Linux machine` | Schedules a Linux poweroff | API key, admin key, and shutdown enabled |

The legacy aliases `What is the item the sursor is pointing at?` and `All avaible comamnds` remain accepted for compatibility, but new clients should use the correctly spelled commands.

The `All available commands` response currently lists the primary operational commands. It does not include the two specialized response commands beginning with `What is the item` and `AI make this person pregnant`, although both remain callable as shown above.

## Python client

The repository includes [`testserver_client.py`](testserver_client.py). It uses only the Python standard library.

Set the client connection values on Linux or macOS:

```bash
export TESTSERVER_URL="https://localhost:7020"
export TESTSERVER_API_KEY="replace-with-a-long-random-key"
export TESTSERVER_ADMIN_KEY="replace-with-a-different-random-key"
```

On Windows PowerShell:

```powershell
$env:TESTSERVER_URL = "https://localhost:7020"
$env:TESTSERVER_API_KEY = "replace-with-a-long-random-key"
$env:TESTSERVER_ADMIN_KEY = "replace-with-a-different-random-key"
```

Start interactive mode:

```bash
python3 testserver_client.py
```

Send one command and exit:

```bash
python3 testserver_client.py --command "Ping"
python3 testserver_client.py --command "Server info"
```

For new clients, use a structured action instead:

```bash
python3 testserver_client.py --action live
python3 testserver_client.py --action ready
python3 testserver_client.py --action info
python3 testserver_client.py --action diagnostics
```

Structured power actions use the administrator key and do not require the standard key:

```bash
python3 testserver_client.py --action restart
python3 testserver_client.py --action shutdown
```

Display all client options:

```bash
python3 testserver_client.py --help
```

Using environment variables for keys is safer than passing keys as command-line arguments, which may expose them through shell history or process listings.

## Test from another device on the same network

The default launch profile listens only on `localhost`, so other devices cannot reach it. For a temporary test on a trusted LAN, start the server on all network interfaces:

```bash
export Authentication__ApiKey="replace-with-a-long-random-key"
export Authentication__AdminApiKey="replace-with-a-different-random-key"
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://0.0.0.0:5000"
dotnet run --no-launch-profile
```

`Development` is specified here because production mode redirects HTTP to HTTPS port 443. This HTTP configuration sends API keys without encryption and must only be used for short-lived testing on a trusted network.

Find the server's LAN address:

```bash
hostname -I
```

For example, if the address is `192.168.1.50`, allow the test port if UFW is active:

```bash
sudo ufw allow 5000/tcp
```

Copy `testserver_client.py` to the other device. Configure it with the server's LAN address:

Linux or macOS:

```bash
export TESTSERVER_URL="http://192.168.1.50:5000"
export TESTSERVER_API_KEY="replace-with-a-long-random-key"
export TESTSERVER_ADMIN_KEY="replace-with-a-different-random-key"
python3 testserver_client.py --command "Ping"
```

Windows PowerShell:

```powershell
$env:TESTSERVER_URL = "http://192.168.1.50:5000"
$env:TESTSERVER_API_KEY = "replace-with-a-long-random-key"
$env:TESTSERVER_ADMIN_KEY = "replace-with-a-different-random-key"
python testserver_client.py --command "Ping"
```

The expected response is:

```text
HTTP 200
{
  "message": "Pong"
}
```

If the request cannot connect, check that:

- Both devices are on the same network and can reach each other.
- The client uses the server's LAN address, not `localhost`.
- TCP port 5000 is allowed by the host and network firewalls.
- The server is still running and reports that it is listening on `0.0.0.0:5000`.
- Guest or client isolation is disabled on the Wi-Fi network.

Remove the temporary UFW rule after testing if it is no longer needed:

```bash
sudo ufw delete allow 5000/tcp
```

## Test from Android with Termux

Install a current Termux build from [F-Droid](https://f-droid.org/packages/com.termux/) or the official [Termux GitHub releases](https://github.com/termux/termux-app/releases). Termux also has an experimental Google Play branch for Android 11 or newer, but the official project notes that it has missing functionality and bugs compared with the stable F-Droid build. Legacy Google Play builds remain unsupported. If you use Termux add-ons, install Termux and every add-on from the same source because builds from different sources use different signatures.

Connect the Android device to the same trusted Wi-Fi network as the server. In Termux, update the packages and install Python:

```bash
pkg update
pkg upgrade
pkg install python
python --version
```

The client requires Python 3.10 or newer and does not require any `pip` packages.

Copy `testserver_client.py` into the Android device's **Download** folder. Then give Termux shared-storage access and copy the file into the Termux home directory:

```bash
termux-setup-storage
cp ~/storage/downloads/testserver_client.py ~/
cd ~
```

Android should display a storage permission prompt after `termux-setup-storage`. If the downloaded file has a suffix such as `(1)`, use its actual filename in the `cp` command.

Configure the client with the server's LAN address and the same keys used by the server. Replace `192.168.1.50` with the server's actual address:

```bash
export TESTSERVER_URL="http://192.168.1.50:5000"
export TESTSERVER_API_KEY="replace-with-a-long-random-key"
export TESTSERVER_ADMIN_KEY="replace-with-a-different-random-key"
```

Send a safe test command:

```bash
python testserver_client.py --command "Ping"
```

The expected output is:

```text
Warning: HTTP sends API keys without encryption.
HTTP 200
{
  "message": "Pong"
}
```

Start interactive mode with:

```bash
python testserver_client.py
```

Type `quit`, `exit`, or an empty line to leave interactive mode. Exported variables last only for the current Termux shell session, so set them again after opening a new session.

If the connection fails:

- Confirm that the server is running with the temporary LAN configuration on `0.0.0.0:5000`.
- Confirm that Android is using the same Wi-Fi network rather than mobile data.
- Check the server IP address again because DHCP may have changed it.
- Disable any VPN temporarily if it prevents access to local network addresses.
- Check the host firewall and the Wi-Fi router's guest/client-isolation setting.

If the server has been separately configured to listen for HTTPS connections on the LAN, use its `https://` URL normally when its certificate is publicly trusted. For a self-signed certificate during local testing only, add `--insecure`:

```bash
python testserver_client.py \
  --url "https://192.168.1.50:7020" \
  --insecure \
  --command "Ping"
```

Do not use `--insecure` for an internet-accessible deployment because it disables certificate verification.

## HTTPS for real deployments

Do not expose the temporary HTTP configuration to the internet. Use a valid TLS certificate and either configure Kestrel for HTTPS or place the application behind a properly configured reverse proxy such as Caddy or Nginx.

In non-development environments, the application enables HSTS and redirects HTTP requests to HTTPS port 443. If a reverse proxy terminates TLS, forwarded headers and trusted proxy addresses must be configured before relying on the original client IP or scheme. Without that configuration, all clients may appear to have the proxy's IP address and therefore share one rate-limit bucket.

## Restart and shutdown

Both operations are disabled in `appsettings.json` by default.

Set the following environment variables before starting the server. If the server is already running, stop and restart it after changing them.

Enable restart through environment variables:

```bash
export Restart__Enabled=true
export Restart__DelayMinutes=1
```

Enable shutdown through environment variables:

```bash
export Shutdown__Enabled=true
export Shutdown__DelayMinutes=1
```

The configured delay is clamped to a range of 1 through 60 minutes. The preferred structured actions require only `TESTSERVER_ADMIN_KEY`:

```bash
python3 testserver_client.py --action restart
python3 testserver_client.py --action shutdown
```

For compatibility, the legacy commands below require both client keys:

```bash
python3 testserver_client.py --command "Restart Linux machine"
python3 testserver_client.py --command "Shutdown Linux machine"
```

The server runs the Linux `shutdown` executable directly with either `--reboot` or `--poweroff`. The account running TestServer must have permission to schedule the operation. The server waits at most 10 seconds for the scheduling command itself to finish.

Cancel a scheduled restart or shutdown on the server before its delay expires:

```bash
sudo shutdown -c
```

Test power commands only on a disposable virtual machine or a machine that may safely restart or power off.

## Manual API request

When the temporary LAN configuration on port 5000 is running, the request body must be JSON with a `message` property:

```bash
curl --request POST "http://127.0.0.1:5000/api/message" \
  --header "Content-Type: application/json" \
  --header "X-API-Key: replace-with-a-long-random-key" \
  --data '{"message":"Ping"}'
```

For restart or shutdown, also provide:

```text
X-Admin-API-Key: replace-with-a-different-random-key
```

## Expected status codes

| Status | Meaning |
|---|---|
| `200` | Command completed successfully |
| `202` | Restart or shutdown was successfully scheduled |
| `400` | Empty, oversized, malformed, or unknown command |
| `401` | Missing or incorrect key required by the selected route |
| `403` | Power operation is disabled or the administrator key is invalid |
| `499` | Command processing was cancelled after the client disconnected; the disconnected client may not receive this response |
| `429` | The applicable per-IP rate limit was exceeded (30 requests per minute for standard and legacy routes, or 5 for structured power routes) |
| `500` | Command processing or host operation failed |
| `501` | Restart or shutdown was requested on a non-Linux host |
| `503` | The key required by the selected route is not configured |

## Safe test checklist

Run these tests before enabling any power operation:

1. Send `Ping` with the correct key and expect HTTP `200` with `Pong`.
2. Remove or change the normal key and expect HTTP `401`.
3. Send an unknown command and expect HTTP `400`.
4. Send `Restart Linux machine` while restart is disabled and expect HTTP `403`.
5. Send `Shutdown Linux machine` while shutdown is disabled and expect HTTP `403`.
6. If testing administrator authorization, enable the operation only on a disposable VM, omit the admin key, and expect HTTP `403`.

The standard and legacy API limiter allows 30 requests per minute for each directly observed remote IP address. Structured power routes allow 5 requests per minute. Neither limiter queues excess requests, and `429` responses include `Retry-After` when the limiter provides it.
