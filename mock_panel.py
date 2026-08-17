from __future__ import annotations

import secrets
from dataclasses import asdict, dataclass
from datetime import datetime, timezone


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


@dataclass(frozen=True)
class MockOrder:
    order_id: str
    provider: str
    service: str
    target: str
    quantity: int
    status: str
    mode: str
    created_at: str

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


class JustAnotherPanelMock:
    """Local-only prank adapter. It never performs an HTTP request."""

    provider = "justanotherpanel"

    def create_order(self, username: str, quantity: int) -> MockOrder:
        if quantity < 1:
            raise ValueError("Die simulierte Menge muss mindestens 1 sein.")

        return MockOrder(
            order_id=f"JAP-DEMO-{secrets.randbelow(900000) + 100000}",
            provider=self.provider,
            service="Instagram Followers (Simulation)",
            target=f"https://instagram.com/{username}",
            quantity=quantity,
            status="Pending",
            mode="dry_run",
            created_at=utc_now(),
        )

