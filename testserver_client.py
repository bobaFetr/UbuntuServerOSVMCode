#!/usr/bin/env python3
"""Portable command-line client for TestServer."""

import argparse
import json
import os
import ssl
import sys
import urllib.error
import urllib.request


def send_command(
    base_url: str,
    api_key: str,
    command: str,
    admin_key: str | None = None,
    timeout: float = 15,
    insecure: bool = False,
) -> tuple[int, object]:
    url = f"{base_url.rstrip('/')}/api/message"
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json",
        "X-API-Key": api_key,
    }
    if admin_key:
        headers["X-Admin-API-Key"] = admin_key

    request = urllib.request.Request(
        url,
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
    parser = argparse.ArgumentParser(description="Send commands to TestServer.")
    parser.add_argument(
        "--url",
        default=os.getenv("TESTSERVER_URL", "https://localhost:7020"),
        help="Server base URL (or set TESTSERVER_URL).",
    )
    parser.add_argument(
        "--api-key",
        default=os.getenv("TESTSERVER_API_KEY"),
        help="API key (prefer TESTSERVER_API_KEY).",
    )
    parser.add_argument(
        "--admin-key",
        default=os.getenv("TESTSERVER_ADMIN_KEY"),
        help="Admin key for shutdown/restart (prefer TESTSERVER_ADMIN_KEY).",
    )
    parser.add_argument("--command", "-c", help="Send one command and exit.")
    parser.add_argument("--timeout", type=float, default=15, help="Timeout in seconds.")
    parser.add_argument(
        "--insecure",
        action="store_true",
        help="Skip TLS certificate verification (local testing only).",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.api_key:
        print("Missing API key. Set TESTSERVER_API_KEY or use --api-key.", file=sys.stderr)
        return 2

    if args.url.startswith("http://"):
        print("Warning: HTTP sends API keys without encryption.", file=sys.stderr)

    commands = [args.command] if args.command else None
    while True:
        if commands is not None:
            command = commands[0]
        else:
            try:
                command = input("command (or 'quit'): ").strip()
            except (EOFError, KeyboardInterrupt):
                print()
                return 0

        if not command or command.lower() in {"quit", "exit"}:
            return 0

        try:
            status, response = send_command(
                args.url,
                args.api_key,
                command,
                args.admin_key,
                args.timeout,
                args.insecure,
            )
            print_response(status, response)
        except (urllib.error.URLError, TimeoutError) as error:
            print(f"Connection failed: {error}", file=sys.stderr)
            return 1

        if commands is not None:
            return 0 if 200 <= status < 300 else 1


if __name__ == "__main__":
    raise SystemExit(main())
