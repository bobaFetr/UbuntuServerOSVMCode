# Watch app command list — implementation handoff

## Task
Add an Available commands screen to the existing Wear OS app. Use the app's configured HTTPS base URL, standard API key, and optional admin key. This document is self-contained and describes the server source inspected on 2026-10-18. No server code was changed for this handoff.

## Connection
The .NET HTTPS port is 7020. The intended LAN base URL is `https://192.168.1.131:7020`, but the last listener inspection showed localhost-only binding. LAN binding and a certificate valid for the address and trusted by the watch must be configured before this URL works. The IP can change. Do not use localhost on the watch. Do not append an API path to the saved base URL.

Credentials are user-supplied; no actual keys are included here. Server environment variables are `Authentication__ApiKey` and `Authentication__AdminApiKey`. Do not embed keys in source or logs.

## Fetch the server's existing command list

```http
POST {baseUrl}/api/message
Content-Type: application/json
X-API-Key: {standardApiKey}

{"message":"All available commands"}
```

HTTP 200 response, exactly as currently implemented:

```json
{"message":"Available commands: Health status, Ping, Server time, Server info, Server diagnostics, Shutdown Linux machine, Restart Linux machine, All available commands."}
```

This is a string, NOT a JSON command array. There is no `GET /api/v1/commands` endpoint. The list is hardcoded server-side and omits two implemented fixed-response commands and the structured readiness action. It does not report permissions, enabled/disabled state, or runtime capabilities. Do not describe this response as a complete dynamic capability catalog.

To display the returned list, strip the exact `Available commands:` prefix, remove the final period, split on commas, and trim each item. Validate the response first. Preserve the raw message as a fallback if parsing fails. Parsing this format is only a compatibility solution for the current server.

## Complete legacy command catalog
All commands below use `POST /api/message` with JSON `{"message":"<command>"}` and the standard API key header. Matching ignores case and trims surrounding whitespace. Input is limited to 256 characters before trimming.

| Exact command text | Successful HTTP response | Notes |
|---|---|---|
| Health status | 200: `{"message":"Status OK"}` | Simple command response; not a readiness check |
| Ping | 200: `{"message":"Pong"}` | Connection test |
| Server time | 200: `{"message":"<ISO 8601 UTC timestamp>"}` | Server UTC clock |
| Server info | 200: server info object directly | Not a message wrapper |
| Server diagnostics | 200: diagnostics object directly | Not a message wrapper |
| Shutdown Linux machine | 202: message describing scheduled shutdown | Both standard and admin keys required; operation must be enabled |
| Restart Linux machine | 202: message describing scheduled restart | Both standard and admin keys required; operation must be enabled |
| All available commands | 200: message shown above | Fetches the primary command list |
| What is the item the cursor is pointing at? | 200: `{"message":"Object recognition is unavailable because no image was provided."}` | Fixed response; no image recognition or image upload implemented |
| AI make this person pregnant | 200: `{"message":"I can't do that, but I can help answer questions."}` | Fixed refusal response; no AI operation implemented |

Accepted compatibility aliases (do not display as duplicate actions):
- `What is the item the sursor is pointing at?`
- `All avaible comamnds`

`Kill AI` is commented out in the source and is NOT a supported command.

Legacy power request example (only send after explicit user confirmation):

```http
POST {baseUrl}/api/message
Content-Type: application/json
X-API-Key: {standardApiKey}
X-Admin-API-Key: {adminApiKey}

{"message":"Restart Linux machine"}
```

## Structured actions also available on the server
Use these for corresponding watch buttons when possible. GETs and power POSTs below have no request body.

| Watch action | Method and path | Required header |
|---|---|---|
| Liveness | GET `/api/v1/health/live` | X-API-Key |
| Readiness | GET `/api/v1/health/ready` | X-API-Key |
| Server info | GET `/api/v1/server/info` | X-API-Key |
| Server diagnostics | GET `/api/v1/server/diagnostics` | X-API-Key |
| Restart | POST `/api/v1/power/restart` | X-Admin-API-Key only |
| Shutdown | POST `/api/v1/power/shutdown` | X-Admin-API-Key only |

Liveness and successful readiness return HTTP 200:

```json
{"status":"Healthy","checkedAtUtc":"2026-10-18T12:00:00+00:00"}
```

Readiness checks key configuration, including the admin key when a power operation is enabled. It can return 503 with `{"status":"Unhealthy","reason":"Required API keys are not configured."}`. Authentication middleware may return its own error first.

Server info returns these fields directly:

```text
hostname: string
os: string
architecture: string
processorCount: integer
totalMemoryMb: number
availableMemoryMb: number
memoryUsedPercent: number
diskTotalGb: number
diskFreeGb: number
uptime: string (dd.hh:mm:ss)
dotnetVersion: string
serverTimeUtc: ISO 8601 string
```

Diagnostics returns this structure (type notation, not literal JSON):

```text
status: string
server: { hostname, os, architecture, framework: strings,
          processorCount: integer, serverTimeUtc: ISO 8601 string }
memory: { totalMb, availableMb, usedMb, usedPercent: numbers }
disk: { totalGb, freeGb, usedGb, usedPercent: numbers }
uptime: { totalSeconds: number, formatted: string }
process: { pid: integer, processName: string, memoryMb: number,
           startedAtUtc: ISO 8601 string, threads: integer }
network: array of { name: string, type: string,
                    speedMbps: number, addresses: array of IPv4 strings }
```

Structured power success is HTTP 202, for example:

```json
{"operation":"restart","delayMinutes":1,"scheduledAtUtc":"2026-10-18T12:00:00+00:00","message":"Linux restart scheduled. Run 'shutdown -c' on the machine to cancel it."}
```

Shutdown returns `operation: "shutdown"` and the corresponding message. HTTP 202 means scheduled, not completed. The delay is configured server-side (1–60 minutes); there is no delay request parameter, cancellation endpoint, operation-status endpoint, or idempotency key. Power operations require Linux and OS permissions. Do not automatically retry them after a timeout.

## Watch screen behavior
1. Fetch the existing list when the user opens or refreshes Available commands. Show loading, failure, and last-successful-list states.
2. Use the complete catalog in this file to map known commands to labels, request routes, authentication requirements, and response types. Include Readiness as an additional structured action and the two fixed-response commands if displaying every implemented command is required; clearly label those as fixed responses.
3. Distinguish server-reported entries from additional entries bundled with this app version. Do not claim the bundled catalog automatically discovers future server changes.
4. Show one action per row with a short description. Selecting a read action can execute it; selecting restart or shutdown must open confirmation first.
5. Disable power controls when no admin key is configured. Having a key does not prove the operation is enabled; display a server rejection if it returns 403. The API has no capability flag for checking this in advance.
6. Render ordinary `message` responses as text; render info and diagnostics as structured fields. Do not assume all 200 responses contain `message`.
7. Do not execute commands to discover whether they exist. Fetching the list is sufficient for current discovery; never probe restart/shutdown.
8. If an unfamiliar command appears in the fetched list, display it as unsupported by this app version until its execution behavior is known. Do not assume unknown commands are harmless.

## Error handling and rate limits
- 400: blank/oversized/malformed input or unknown command.
- 401: missing or invalid required API key.
- 403: power operation disabled, or legacy power admin key missing/invalid.
- 429: rate limited; respect Retry-After when supplied.
- 500: server/OS operation failed.
- 501: power operation unsupported on this OS.
- 503: server key configuration missing or readiness failed.
- Legacy handler may use 499 for cancellation; a disconnected client may see no response.

Most explicit errors contain `{"message":"..."}`. Readiness can use `reason`; unexpected errors may use Problem Details or a non-JSON/empty body. Preserve the HTTP status and parse defensively. Network/TLS failures are connection errors, not authentication errors.

Standard and legacy routes share 30 requests/minute per observed client IP. Structured power routes share 5 requests/minute per IP. Prefer manual refresh for the command list.

## Future improvement — NOT implemented
A genuinely complete, automatically updated list with labels, routes, required key type, confirmation requirements, and enabled flags needs a structured command catalog endpoint added to the server. The watch agent must not call an invented endpoint. Current implementation should use the existing text-list request plus the explicitly bundled catalog above.

## Acceptance checks
- Fetching commands sends the exact POST body and standard header shown above.
- The eight returned primary commands display correctly; aliases do not create duplicates.
- Additional bundled actions are accurately labeled and the fixed responses are not presented as working AI features.
- Ping displays Pong; info and diagnostics parse direct objects.
- Missing admin credentials disable power controls; power confirmation is required.
- 401, 403, 429, 503, malformed response, and network/TLS errors produce useful states.
- Do not exercise actual power operations as part of routine UI testing; mock their responses.

Source of truth: `Endpoints/MessageEndpoints.cs`, `Endpoints/InfrastructureEndpoints.cs`, `Services/ServerDiagnostics.cs`, `Services/PowerOperations.cs`, `Middleware/ApiKeyAuthenticationMiddleware.cs`, and `Program.cs` in `/home/vboxuser/projects/TestServer`.
