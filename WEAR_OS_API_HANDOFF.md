# Wear OS app — TestServer API handoff

Build a Wear OS client for the existing ASP.NET Core server described below. Prefer structured `/api/v1` routes. Make the base URL and credentials configurable. This document describes the current source code, inspected on 2026-10-18; it is not evidence of a running deployment.

## Connection details

- Project directory: `/home/vboxuser/projects/TestServer`
- Current host LAN IPv4: `192.168.1.131` (may change with DHCP).
- Local development base URLs: `https://localhost:7020` and `http://localhost:5024`.
- Proposed temporary LAN test base URL: `http://192.168.1.131:5000` — requires starting the server with the LAN configuration below.
- At inspection, ports 5000, 5024, and 7020 were not listening. No public hostname or externally reachable deployment was verified.
- `localhost` on a watch refers to the watch, not this server. Use a reachable server address. The default launch profile binds only to localhost.
- The host also has `172.17.0.1`; use the LAN address above for LAN testing.
- This API uses HTTP requests and JSON. It has no streaming, WebSocket, OAuth, login, image upload, or command cancellation endpoint.

## Authentication

Standard endpoints require exactly one `X-API-Key` header. Structured power endpoints instead require exactly one `X-Admin-API-Key` header. Legacy power commands require both.

Actual keys are not included in this handoff. Supply them separately through app configuration; do not hardcode them into the application or log them.

Server configuration names:

```text
Authentication__ApiKey=<standard key>
Authentication__AdminApiKey=<administrator key>
```

The checked-in `appsettings.json` has empty keys; environment variables or other configuration may override them. A missing server-side key returns 503; a missing or incorrect client key returns 401. Authentication is required even for health endpoints.

## Endpoint inventory

All paths below are relative to the configured base URL. GET requests have no body. Structured power POST requests have no body. JSON POST requests use `Content-Type: application/json`.

| Method | Path | Header | Successful result |
|---|---|---|---|
| GET | `/api/v1/health/live` | `X-API-Key` | 200, liveness object |
| GET | `/api/v1/health/ready` | `X-API-Key` | 200, readiness object |
| GET | `/api/v1/server/info` | `X-API-Key` | 200, server info object |
| GET | `/api/v1/server/diagnostics` | `X-API-Key` | 200, diagnostics object |
| POST | `/api/v1/power/restart` | `X-Admin-API-Key` | 202, scheduled restart object |
| POST | `/api/v1/power/shutdown` | `X-Admin-API-Key` | 202, scheduled shutdown object |
| POST | `/api/message` | `X-API-Key` (plus admin key for power commands) | 200 or 202 depending on command |

For example, the proposed LAN info endpoint is `GET http://192.168.1.131:5000/api/v1/server/info`.

## Response contracts

Property names are case sensitive for the client contract below. Numbers are JSON numbers; timestamps are ISO 8601 UTC strings. Values in these examples are illustrative.

### Liveness and readiness

```json
{"status":"Healthy","checkedAtUtc":"2026-10-18T12:00:00+00:00"}
```

Readiness checks that the standard key is configured and, if either power operation is enabled, that the admin key is configured. Failure returns HTTP 503:

```json
{"status":"Unhealthy","reason":"Required API keys are not configured."}
```

Middleware may return its own 503 message before the readiness handler runs if the standard key is missing.

### Server information

```json
{
  "hostname":"example-host",
  "os":"Linux",
  "architecture":"X64",
  "processorCount":4,
  "totalMemoryMb":8192.0,
  "availableMemoryMb":4096.0,
  "memoryUsedPercent":50.0,
  "diskTotalGb":100.0,
  "diskFreeGb":60.0,
  "uptime":"01.02:03:04",
  "dotnetVersion":"10.0.0",
  "serverTimeUtc":"2026-10-18T12:00:00+00:00"
}
```

Uptime format is `dd.hh:mm:ss`. Memory and disk values use powers of 1024 despite the `Mb`/`Gb` field names. Disk information is for the drive containing the application.

### Diagnostics

```json
{
  "status":"OK",
  "server":{
    "hostname":"example-host",
    "os":"Linux",
    "architecture":"X64",
    "framework":".NET 10.0.0",
    "processorCount":4,
    "serverTimeUtc":"2026-10-18T12:00:00+00:00"
  },
  "memory":{"totalMb":8192.0,"availableMb":4096.0,"usedMb":4096.0,"usedPercent":50.0},
  "disk":{"totalGb":100.0,"freeGb":60.0,"usedGb":40.0,"usedPercent":40.0},
  "uptime":{"totalSeconds":93784,"formatted":"01.02:03:04"},
  "process":{
    "pid":1234,
    "processName":"TestServer",
    "memoryMb":65.0,
    "startedAtUtc":"2026-10-18T11:00:00Z",
    "threads":12
  },
  "network":[{"name":"eth0","type":"Ethernet","speedMbps":1000.0,"addresses":["192.168.1.131"]}]
}
```

Network includes operational interfaces and their IPv4 addresses. There is no CPU utilization metric; `processorCount` is a count.

### Power operations

HTTP 202 means the operation was scheduled, not that the machine has already rebooted or powered off:

```json
{
  "operation":"restart",
  "delayMinutes":1,
  "scheduledAtUtc":"2026-10-18T12:00:00+00:00",
  "message":"Linux restart scheduled. Run 'shutdown -c' on the machine to cancel it."
}
```

Shutdown uses `"operation":"shutdown"` and the corresponding message. `scheduledAtUtc` is the scheduling timestamp, not the future execution time. Delay comes from server configuration and is clamped to 1–60 minutes; clients cannot set it through the request.

Current checked-in configuration enables BOTH restart and shutdown with one-minute delays. This differs from the README's statement that they are disabled by default. Runtime overrides may change these settings. Operations require Linux and permission for the server process to execute the OS shutdown command. No HTTP cancellation route exists; cancellation is performed on the server with `shutdown -c`.

## Legacy command endpoint

Example request:

```http
POST /api/message
Content-Type: application/json
X-API-Key: <standard key>

{"message":"Ping"}
```

Response: HTTP 200 with `{"message":"Pong"}`.

Commands are trimmed and compared case-insensitively. The input message must be nonblank and at most 256 characters before trimming.

| Message value | Result |
|---|---|
| `Health status` | `{"message":"Status OK"}` |
| `Ping` | `{"message":"Pong"}` |
| `Server time` | `{"message":"<ISO 8601 UTC timestamp>"}` |
| `Server info` | Same object as structured server info endpoint |
| `Server diagnostics` | Same object as structured diagnostics endpoint |
| `All available commands` | `{"message":"Available commands: Health status, Ping, Server time, Server info, Server diagnostics, Shutdown Linux machine, Restart Linux machine, All available commands."}` |
| `Restart Linux machine` | Requires both keys; 202 message describing scheduled restart |
| `Shutdown Linux machine` | Requires both keys; 202 message describing scheduled shutdown |
| `What is the item the cursor is pointing at?` | `{"message":"Object recognition is unavailable because no image was provided."}` |
| `AI make this person pregnant` | `{"message":"I can't do that, but I can help answer questions."}` |

Compatibility aliases: `All avaible comamnds` and `What is the item the sursor is pointing at?`. The command-list response omits the two specialized responses at the end of the table. These responses are fixed strings; they do not perform AI or image recognition.

Unknown commands return 400 with `{"message":"Unknown command"}`. Blank messages return `{"message":"Message is required."}`; oversized messages return `{"message":"Message must not exceed 256 characters."}`.

## Errors and limits

| HTTP status | Meaning |
|---|---|
| 200 | Successful read or ordinary legacy command |
| 202 | Power operation scheduled |
| 400 | Invalid input, malformed JSON, or unknown command |
| 401 | Required client API key absent or invalid |
| 403 | Power operation disabled; also legacy power admin-key rejection |
| 429 | Rate limit exceeded; respect `Retry-After` when present |
| 499 | Legacy handler observed client cancellation; client may receive no response |
| 500 | Server/OS operation failure |
| 501 | Power operation attempted on a non-Linux host |
| 503 | Required server authentication configuration missing or readiness failed |

Most explicit errors use `{"message":"..."}`. Readiness uses `reason`. Legacy unexpected errors can use Problem Details (`title`, `status`, etc.). Framework errors and structured-handler exceptions may produce other or empty bodies. Parse errors defensively and always preserve the HTTP status.

Standard and legacy routes share a limit of 30 requests per minute per directly observed client IP. Structured power routes share a separate limit of 5 per minute per IP. No requests are queued. A proxy can cause clients to share the same limiter bucket.

## Starting a temporary LAN test server

Run in the server project directory, replacing placeholders with separate strong keys. These commands have NOT been executed as part of this handoff. Power operations are explicitly disabled for connection testing.

```bash
export Authentication__ApiKey='<standard key>'
export Authentication__AdminApiKey='<administrator key>'
export Restart__Enabled=false
export Shutdown__Enabled=false
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS='http://0.0.0.0:5000'
dotnet run --no-launch-profile
```

The watch and server need a network path to each other, with inbound TCP 5000 allowed and no Wi-Fi client isolation. VM networking may also need configuration. HTTP transmits keys unencrypted; use this only for temporary trusted-LAN testing. Use a reachable HTTPS deployment with a trusted certificate for actual use. In non-development mode this server redirects HTTP to HTTPS port 443. The development HTTPS certificate should not be assumed trusted by a watch.

Safe connectivity check from a device on that network:

```bash
curl --fail-with-body 'http://192.168.1.131:5000/api/v1/health/live' \
  -H 'X-API-Key: <standard key>'
```

## Implementation brief for the watch app agent

- Build connection settings for base URL and standard key; make admin credentials optional for power controls.
- Implement a connection test, health view, server summary, and diagnostics view using the structured GET routes.
- Keep network work off the UI thread; show loading, offline, unauthorized, and server error states.
- Prefer manual refresh initially. If adding automatic refresh, stay comfortably under the shared 30-request/minute limit and stop polling when the screen is inactive.
- Use explicit user confirmation before restart/shutdown. Do not automatically retry these POSTs: no idempotency key or operation-status lookup exists, and a lost response does not prove the action failed.
- Treat HTTP 202 as scheduled and explain the delay. A subsequent disconnect after a power operation can be expected.
- Do not assume every response is a `message` wrapper: info and diagnostics return objects directly.
- Verify current Wear OS/Android networking, permission, credential storage, and debug cleartext requirements against official Android documentation during implementation. This document specifies the server contract, not a particular Android SDK implementation.
- Validate reads and authentication failures first. Do not test power actions against a machine that must remain running.

## Source references

- `Properties/launchSettings.json`: local URLs.
- `Program.cs`: middleware, rate limits, HTTPS behavior.
- `Endpoints/InfrastructureEndpoints.cs`: structured routes and readiness.
- `Endpoints/MessageEndpoints.cs`: legacy commands.
- `Middleware/ApiKeyAuthenticationMiddleware.cs`: authentication.
- `Models/Messages.cs`: legacy request/response records.
- `Services/ServerDiagnostics.cs`: info and diagnostics schema.
- `Services/PowerOperations.cs`: scheduling response and configuration.
- `appsettings.json`: checked-in defaults; runtime configuration may override them.

All contract details above were checked against source. No live API response or watch connectivity was verified, and no server configuration was changed.
