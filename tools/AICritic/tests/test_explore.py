"""The Playtest Controller's Python side: action validation, scenarios and the mailbox loop (fake client)."""

import copy
import json
import os
import shutil
import stat
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from orchestrator import BLOCKED, PASS, config, explore, playtest  # noqa: E402

LOCATIONS = {"mirewatch_square": (0, -148), "mere_east": (58, -84)}


class ValidationTests(unittest.TestCase):
    def test_accepts_a_valid_move_with_defaults(self):
        self.assertEqual(playtest.validate_action("move", {"direction": "forward", "seconds": 2}, LOCATIONS),
                         {"direction": "forward", "seconds": 2.0, "sprint": False})

    def test_nulls_from_the_explorer_schema_are_ignored(self):
        clean = playtest.validate_action("attack", {"seconds": None, "slot": None}, LOCATIONS)
        self.assertEqual(clean, {"seconds": 6})

    def test_rejects_unknown_actions(self):
        for name in ("gm", "teleport", "quit", "shell", "exec"):
            with self.assertRaises(playtest.ActionError):
                playtest.validate_action(name, {}, LOCATIONS)

    def test_rejects_out_of_range_and_wrong_types(self):
        bad = [("move", {"direction": "up", "seconds": 1}), ("move", {"direction": "forward", "seconds": 60}),
               ("wait", {"seconds": True}), ("cast", {"slot": 1.5}), ("cast", {"slot": 13}),
               ("open_window", {"window": "settings"}), ("goto", {"location": "the moon"}),
               ("target", {"name": "x; rm -rf /"}), ("camera", {}), ("jump", {"seconds": 1})]
        for name, params in bad:
            with self.assertRaises(playtest.ActionError, msg=f"{name} {params}"):
                playtest.validate_action(name, params, LOCATIONS)

    def test_goto_and_reset_expand_to_controller_commands(self):
        scenario = {"locations": LOCATIONS, "start": "mirewatch_square"}
        self.assertEqual(playtest.to_commands("goto", {"location": "mere_east"}, scenario),
                         [{"action": "teleport", "x": 58, "z": -84}])
        reset = playtest.to_commands("reset", {}, scenario)
        self.assertEqual([c["action"] for c in reset], ["close_windows", "gm", "gm", "teleport", "gm"])
        self.assertTrue(all(c["text"].startswith(".") for c in reset if c["action"] == "gm"))

    def test_camera_marks_the_given_values(self):
        cmd = playtest.to_commands("camera", {"pitch": 30.0}, {"locations": LOCATIONS})[0]
        self.assertEqual(cmd, {"action": "camera", "pitch": 30.0, "has_pitch": True})

    def test_every_scenario_loads_and_its_script_validates(self):
        for name in explore.SCENARIO_NAMES:
            s = playtest.load_scenario(name)
            self.assertIn(s["start"], s["locations"])
            self.assertTrue(s.get("script"), name)

    def test_explorer_schema_covers_every_parameter(self):
        schema = explore.explorer_schema()
        params = schema["properties"]["params"]
        names = {k for spec in playtest.ACTIONS.values() for k in spec}
        self.assertEqual(set(params["required"]), names)
        self.assertEqual(schema["properties"]["action"]["enum"], list(playtest.ACTIONS))


class FakeClientExploreTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="aicritic-explore-"))
        self.cfg = copy.deepcopy(config.DEFAULTS)
        self.cfg["paths"]["runs_dir"] = str(self.tmp / "runs")
        self.cfg["paths"]["worktree_root"] = str(self.tmp / "worktrees")
        self.cfg["_source"] = "test"

    def tearDown(self):
        def writable(func, path, _exc):
            os.chmod(path, stat.S_IWRITE)
            func(path)
        shutil.rmtree(self.tmp, onexc=writable)

    def test_scripted_exploration_records_every_action_with_a_screenshot(self):
        state = explore.explore(self.cfg, ["smoke", "combat"], dry_run=True, fake_client=True)
        self.assertEqual(state["outcome"]["verdict"], PASS)  # the fixture review
        run_dir = Path(self.cfg["paths"]["runs_dir"]) / state["run_id"]
        doc = json.loads((run_dir / "latest-review.json").read_text(encoding="utf-8"))
        smoke = doc["exploration"]["smoke"]["steps"]
        scripted = [s for s in smoke if s["source"] == "script"]
        self.assertEqual(len(scripted), len(playtest.load_scenario("smoke")["script"]))
        for s in smoke:
            self.assertTrue((run_dir / "round-01" / "evidence" / "explore" / "smoke" / s["screenshot"]).exists())
        steps_file = run_dir / "round-01" / "evidence" / "explore" / "combat" / "steps.jsonl"
        self.assertTrue(steps_file.exists())
        self.assertTrue((run_dir / "round-01" / "evidence" / "screenshots" / "smoke_sheet.png").exists())
        md = (run_dir / "latest-review.md").read_text(encoding="utf-8")
        self.assertIn("## Exploration", md)
        self.assertEqual(state["approval"]["state"], "not_applicable")

    def test_no_review_is_blocked_not_passed(self):
        state = explore.explore(self.cfg, ["smoke"], dry_run=True, fake_client=True, final_review=False)
        self.assertEqual(state["outcome"]["verdict"], BLOCKED)
        self.assertEqual(state["rounds"][0]["reason"], "not_reviewed")

    def test_all_expands_to_the_four_scenarios(self):
        self.assertEqual(explore.expand(["all"]), list(explore.ALL_SCENARIOS))
        with self.assertRaises(SystemExit):
            explore.expand(["moon"])


if __name__ == "__main__":
    unittest.main()
