#!/usr/bin/env python3
"""Portable command-line client for TestServer."""

import argparse
import json
import os
import ssl
import sys
import urllib.error
import urllib.request


ACTIONS = {
    "live": ("GET", "/api/v1/health/live", "standard"),
    "ready": ("GET", "/api/v1/health/ready", "standard"),
    "info": ("GET", "/api/v1/server/info", "standard"),
    "diagnostics": ("GET", "/api/v1/server/diagnostics", "standard"),
    "restart": ("POST", "/api/v1/power/restart", "admin"),
    "shutdown": ("POST", "/api/v1/power/shutdown", "admin"),
}


def send_request(
    base_url: str,
    path: str,
    method: str,
    header_name: str,
    key: str,
    body: object | None = None,
    timeout: float = 15,
    insecure: bool = False,
) -> tuple[int, object]:
    headers = {"Accept": "application/json", header_name: key}
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode("utf-8")
    elif method == "POST":
        data = b""

    request = urllib.request.Request(
        f"{base_url.rstrip('/')}{path}",
        data=data,
        headers=headers,
        method=method,
    )
    context = ssl._create_unverified_context() if insecure else None

    try:
        with urllib.request.urlopen(request, timeout=timeout, context=context) as response:
            return response.status, decode_response(response.read())
    except urllib.error.HTTPError as error:
        return error.code, decode_response(error.read())


def send_command(
    base_url: str,
    api_key: str,
    command: str,
    admin_key: str | None = None,
    timeout: float = 15,
    insecure: bool = False,
) -> tuple[int, object]:
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json",
        "X-API-Key": api_key,
    }
    if admin_key:
        headers["X-Admin-API-Key"] = admin_key

    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/api/message",
        data=json.dumps({"message": command}).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    context = ssl._create_unverified_context() if insecure else None

    try:
        with urllib.request.urlopen(request, timeout=timeout, context=context) as response:
            return response.status, decode_response(response.read())
    except urllib.error.HTTPError as error:
        return error.code, decode_response(error.read())


def send_action(
    base_url: str,
    action: str,
    api_key: str | None,
    admin_key: str | None,
    timeout: float,
    insecure: bool,
) -> tuple[int, object]:
    method, path, access = ACTIONS[action]
    if access == "admin":
        if not admin_key:
            raise ValueError("Missing admin key. Set TESTSERVER_ADMIN_KEY or use --admin-key.")
        return send_request(
            base_url, path, method, "X-Admin-API-Key", admin_key, timeout=timeout, insecure=insecure)

    if not api_key:
        raise ValueError("Missing API key. Set TESTSERVER_API_KEY or use --api-key.")
    return send_request(
        base_url, path, method, "X-API-Key", api_key, timeout=timeout, insecure=insecure)


def decode_response(body: bytes) -> object:
    text = body.decode("utf-8", errors="replace")
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return text


def print_response(status: int, response: object) -> None:
    print(f"HTTP {status}")
    if isinstance(response, (dict, list)):
        print(json.dumps(response, indent=2, ensure_ascii=False))
    else:
        print(response)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Send requests to TestServer.")
    parser.add_argument(
        "--url",
        default=os.getenv("TESTSERVER_URL", "https://localhost:7020"),
        help="Server base URL (or set TESTSERVER_URL).",
    )
    parser.add_argument(
        "--api-key",
        default=os.getenv("TESTSERVER_API_KEY"),
        help="Standard API key (prefer TESTSERVER_API_KEY).",
    )
    parser.add_argument(
        "--admin-key",
        default=os.getenv("TESTSERVER_ADMIN_KEY"),
        help="Admin key for structured power routes and legacy power commands.",
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--command", "-c", help="Send one legacy text command and exit.")
    mode.add_argument(
        "--action",
        choices=sorted(ACTIONS),
        help="Call a versioned structured API route and exit.",
    )
    parser.add_argument("--timeout", type=float, default=15, help="Timeout in seconds.")
    parser.add_argument(
        "--insecure",
        action="store_true",
        help="Skip TLS certificate verification (local testing only).",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.url.startswith("http://"):
        print("Warning: HTTP sends API keys without encryption.", file=sys.stderr)

    try:
        if args.action:
            status, response = send_action(
                args.url,
                args.action,
                args.api_key,
                args.admin_key,
                args.timeout,
                args.insecure,
            )
            print_response(status, response)
            return 0 if 200 <= status < 300 else 1

        if not args.api_key:
            print("Missing API key. Set TESTSERVER_API_KEY or use --api-key.", file=sys.stderr)
            return 2

        if args.command:
            status, response = send_command(
                args.url,
                args.api_key,
                args.command,
                args.admin_key,
                args.timeout,
                args.insecure,
            )
            print_response(status, response)
            return 0 if 200 <= status < 300 else 1

        while True:
            try:
                command = input("command (or 'quit'): ").strip()
            except (EOFError, KeyboardInterrupt):
                print()
                return 0

            if not command or command.lower() in {"quit", "exit"}:
                return 0

            status, response = send_command(
                args.url,
                args.api_key,
                command,
                args.admin_key,
                args.timeout,
                args.insecure,
            )
            print_response(status, response)
    except ValueError as error:
        print(error, file=sys.stderr)
        return 2
    except (urllib.error.URLError, TimeoutError) as error:
        print(f"Connection failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
