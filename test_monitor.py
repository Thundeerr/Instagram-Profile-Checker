import json
import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from unittest.mock import patch

import monitor


class MonitorTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp_dir.cleanup)
        self.state_file = Path(self.temp_dir.name) / "state.json"
        self.events_file = Path(self.temp_dir.name) / "events.jsonl"
        self.config = monitor.Config("test-key", "thunderceo", 30)

    def paths(self):
        return patch.multiple(
            monitor,
            STATE_FILE=self.state_file,
            EVENTS_FILE=self.events_file,
        )

    def test_first_public_check_does_not_emit_transition(self):
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertFalse(monitor.check_once(self.config))
        self.assertFalse(self.events_file.exists())

    def test_private_to_public_emits_one_event(self):
        self.state_file.write_text(
            json.dumps({"username": "thunderceo", "is_private": True}),
            encoding="utf-8",
        )
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertTrue(monitor.check_once(self.config))

        event = json.loads(self.events_file.read_text(encoding="utf-8"))
        self.assertEqual(event["event"], "profile_became_public")
        self.assertEqual(event["mode"], "dry_run")
        self.assertEqual(event["mock_order"]["provider"], "justanotherpanel")
        self.assertEqual(event["mock_order"]["quantity"], 1000)
        self.assertTrue(event["mock_order"]["order_id"].startswith("JAP-DEMO-"))

    def test_public_to_public_does_not_emit_transition(self):
        self.state_file.write_text(
            json.dumps({"username": "thunderceo", "is_private": False}),
            encoding="utf-8",
        )
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertFalse(monitor.check_once(self.config))
        self.assertFalse(self.events_file.exists())

    def test_active_cooldown_suppresses_duplicate_event(self):
        self.state_file.write_text(
            json.dumps({"username": "thunderceo", "is_private": True}),
            encoding="utf-8",
        )
        self.events_file.write_text(
            json.dumps({
                "event": "profile_became_public",
                "username": "thunderceo",
                "detected_at": datetime.now(timezone.utc).isoformat(),
            }) + "\n",
            encoding="utf-8",
        )
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertFalse(monitor.check_once(self.config))
        self.assertEqual(len(self.events_file.read_text(encoding="utf-8").splitlines()), 1)

    def test_expired_cooldown_allows_event(self):
        self.state_file.write_text(
            json.dumps({"username": "thunderceo", "is_private": True}),
            encoding="utf-8",
        )
        self.events_file.write_text(
            json.dumps({
                "event": "profile_became_public",
                "username": "thunderceo",
                "detected_at": (datetime.now(timezone.utc) - timedelta(days=2)).isoformat(),
            }) + "\n",
            encoding="utf-8",
        )
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertTrue(monitor.check_once(self.config))
        self.assertEqual(len(self.events_file.read_text(encoding="utf-8").splitlines()), 2)

    def test_cooldown_is_scoped_per_account(self):
        self.state_file.write_text(
            json.dumps({"username": "thunderceo", "is_private": True}),
            encoding="utf-8",
        )
        self.events_file.write_text(
            json.dumps({
                "event": "profile_became_public",
                "username": "anotheraccount",
                "detected_at": datetime.now(timezone.utc).isoformat(),
            }) + "\n",
            encoding="utf-8",
        )
        with self.paths(), patch.object(
            monitor, "fetch_profile", return_value={"status": True, "username": "thunderceo", "is_private": False}
        ):
            self.assertTrue(monitor.check_once(self.config))

    def test_manual_demo_bypasses_cooldown(self):
        with self.paths():
            self.assertTrue(monitor.record_public_event("thunderceo", 1000, 1440, manual=True))
            self.assertTrue(monitor.record_public_event("thunderceo", 1000, 1440, manual=True))
        self.assertEqual(len(self.events_file.read_text(encoding="utf-8").splitlines()), 2)


if __name__ == "__main__":
    unittest.main()
