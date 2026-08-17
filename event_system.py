from __future__ import annotations

import json
import threading
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso_utc(value: datetime) -> str:
    return value.astimezone(timezone.utc).isoformat(timespec="seconds")


def parse_timestamp(value: object) -> datetime | None:
    if not isinstance(value, str) or not value.strip():
        return None
    try:
        parsed = datetime.fromisoformat(value.strip().replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


@dataclass(frozen=True)
class EventClaim:
    emitted: bool
    remaining_seconds: int
    event: dict[str, Any] | None = None


class JsonlEventStore:
    _lock = threading.Lock()

    def __init__(self, path: Path):
        self.path = path

    def read_events(self) -> list[dict[str, Any]]:
        if not self.path.exists():
            return []
        events: list[dict[str, Any]] = []
        try:
            lines = self.path.read_text(encoding="utf-8").splitlines()
        except OSError:
            return events
        for line in lines:
            if not line.strip():
                continue
            try:
                item = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(item, dict):
                events.append(item)
        return events

    def cooldown_remaining(
        self,
        username: str,
        event_type: str,
        cooldown_minutes: int,
        now: datetime | None = None,
    ) -> int:
        if cooldown_minutes <= 0:
            return 0
        current = (now or utc_now()).astimezone(timezone.utc)
        normalized = username.strip().lstrip("@").lower()
        latest: datetime | None = None
        for event in self.read_events():
            if str(event.get("event", "")) != event_type:
                continue
            if str(event.get("username", "")).strip().lstrip("@").lower() != normalized:
                continue
            timestamp = parse_timestamp(event.get("detected_at"))
            if timestamp is not None and (latest is None or timestamp > latest):
                latest = timestamp
        if latest is None:
            return 0
        remaining = latest + timedelta(minutes=cooldown_minutes) - current
        return max(0, int(remaining.total_seconds() + 0.999))

    def claim(
        self,
        event: dict[str, Any],
        cooldown_minutes: int,
        force: bool = False,
        now: datetime | None = None,
    ) -> EventClaim:
        current = (now or utc_now()).astimezone(timezone.utc)
        username = str(event.get("username", "")).strip().lstrip("@")
        event_type = str(event.get("event", ""))
        with self._lock:
            remaining = 0 if force else self.cooldown_remaining(
                username, event_type, cooldown_minutes, current
            )
            if remaining > 0:
                return EventClaim(False, remaining)
            stored = dict(event)
            stored.setdefault("schema_version", 1)
            stored.setdefault("event_id", str(uuid.uuid4()))
            stored.setdefault("detected_at", iso_utc(current))
            stored["cooldown_minutes"] = cooldown_minutes
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(stored, ensure_ascii=False) + "\n")
            return EventClaim(True, 0, stored)
