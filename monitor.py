from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from mock_panel import JustAnotherPanelMock


BASE_URL = "https://instagram-looter2.p.rapidapi.com/profile2"
RAPIDAPI_HOST = "instagram-looter2.p.rapidapi.com"
ROOT = Path(__file__).resolve().parent
STATE_FILE = ROOT / "state.json"
EVENTS_FILE = ROOT / "events.jsonl"


def load_dotenv(path: Path) -> None:
    """Load simple KEY=VALUE entries without an external dependency."""
    if not path.exists():
        return

    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip().strip('"').strip("'")
        if key:
            os.environ.setdefault(key, value)


@dataclass(frozen=True)
class Config:
    api_key: str
    username: str
    interval_minutes: int
    mock_quantity: int = 1000

    @classmethod
    def from_env(cls) -> "Config":
        load_dotenv(ROOT / ".env")
        api_key = os.getenv("RAPIDAPI_KEY", "").strip()
        username = os.getenv("INSTAGRAM_USERNAME", "thunderceo").strip().lstrip("@")

        try:
            interval_minutes = int(os.getenv("CHECK_INTERVAL_MINUTES", "30"))
            mock_quantity = int(os.getenv("PANEL_SIMULATED_QUANTITY", "1000"))
        except ValueError as exc:
            raise ValueError("Intervall und simulierte Menge muessen ganze Zahlen sein.") from exc

        if not api_key or api_key == "dein_rapidapi_key":
            raise ValueError("RAPIDAPI_KEY fehlt. Kopiere .env.example nach .env und trage den Key ein.")
        if not username:
            raise ValueError("INSTAGRAM_USERNAME darf nicht leer sein.")
        if interval_minutes < 1:
            raise ValueError("CHECK_INTERVAL_MINUTES muss mindestens 1 sein.")
        if mock_quantity < 1:
            raise ValueError("PANEL_SIMULATED_QUANTITY muss mindestens 1 sein.")

        return cls(
            api_key=api_key,
            username=username,
            interval_minutes=interval_minutes,
            mock_quantity=mock_quantity,
        )


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def fetch_profile(config: Config, timeout_seconds: int = 20) -> dict[str, Any]:
    query = urllib.parse.urlencode(
        {
            "username": config.username,
            "fields": "status,username,is_private",
        }
    )
    request = urllib.request.Request(
        f"{BASE_URL}?{query}",
        headers={
            "x-rapidapi-key": config.api_key,
            "x-rapidapi-host": RAPIDAPI_HOST,
            "Accept": "application/json",
        },
        method="GET",
    )

    try:
        with urllib.request.urlopen(request, timeout=timeout_seconds) as response:
            payload = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", errors="replace")[:300]
        raise RuntimeError(f"RapidAPI HTTP {exc.code}: {detail}") from exc
    except urllib.error.URLError as exc:
        raise RuntimeError(f"RapidAPI nicht erreichbar: {exc.reason}") from exc
    except json.JSONDecodeError as exc:
        raise RuntimeError("RapidAPI hat kein gueltiges JSON geliefert.") from exc

    # Some RapidAPI providers wrap their payload in a `body` object. Supporting
    # both formats makes the monitor resilient to provider-side response changes.
    if isinstance(payload, dict) and isinstance(payload.get("body"), dict):
        payload = payload["body"]

    if not isinstance(payload, dict) or not isinstance(payload.get("is_private"), bool):
        raise RuntimeError("In der RapidAPI-Antwort fehlt das Boolean-Feld 'is_private'.")

    return payload


def read_state() -> dict[str, Any]:
    if not STATE_FILE.exists():
        return {}
    try:
        data = json.loads(STATE_FILE.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return data if isinstance(data, dict) else {}


def write_state(username: str, is_private: bool) -> None:
    state = {
        "username": username,
        "is_private": is_private,
        "checked_at": utc_now(),
    }
    STATE_FILE.write_text(json.dumps(state, indent=2) + "\n", encoding="utf-8")


def record_public_event(username: str, quantity: int) -> None:
    order = JustAnotherPanelMock().create_order(username, quantity)
    event = {
        "event": "profile_became_public",
        "username": username,
        "detected_at": utc_now(),
        "mode": "dry_run",
        "mock_order": order.to_dict(),
    }
    with EVENTS_FILE.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(event, ensure_ascii=False) + "\n")

    print("\a", end="", flush=True)
    print(f"\n*** @{username} IST JETZT OEFFENTLICH ***")
    print(f"Demo-Order: {order.order_id} | {order.quantity} | {order.status}")
    print(f"Dry-Run-Ereignis gespeichert: {EVENTS_FILE}")


def check_once(config: Config) -> bool:
    previous = read_state()
    profile = fetch_profile(config)
    is_private = profile["is_private"]
    timestamp = datetime.now().astimezone().strftime("%Y-%m-%d %H:%M:%S")
    label = "privat" if is_private else "oeffentlich"
    print(f"[{timestamp}] @{config.username}: {label}")

    was_private = (
        previous.get("username") == config.username
        and previous.get("is_private") is True
    )
    write_state(config.username, is_private)

    became_public = was_private and not is_private
    if became_public:
        record_public_event(config.username, config.mock_quantity)
    return became_public


def run_forever(config: Config) -> None:
    interval_seconds = config.interval_minutes * 60
    print(
        f"Ueberwache @{config.username} alle {config.interval_minutes} Minuten. "
        "Beenden mit Strg+C."
    )
    while True:
        try:
            check_once(config)
        except RuntimeError as exc:
            print(f"[{datetime.now().astimezone():%Y-%m-%d %H:%M:%S}] Fehler: {exc}", file=sys.stderr)
        time.sleep(interval_seconds)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Instagram Public-Status-Monitor via RapidAPI")
    parser.add_argument("--once", action="store_true", help="Einmal pruefen und beenden")
    parser.add_argument("--demo", action="store_true", help="Lokale Demo-Order ohne API-Aufruf erzeugen")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        if args.demo:
            load_dotenv(ROOT / ".env")
            username = os.getenv("INSTAGRAM_USERNAME", "thunderceo").strip().lstrip("@")
            quantity = int(os.getenv("PANEL_SIMULATED_QUANTITY", "1000"))
            record_public_event(username, quantity)
            return 0

        config = Config.from_env()
        if args.once:
            check_once(config)
        else:
            run_forever(config)
    except KeyboardInterrupt:
        print("\nMonitor beendet.")
    except (ValueError, RuntimeError) as exc:
        print(f"Fehler: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
