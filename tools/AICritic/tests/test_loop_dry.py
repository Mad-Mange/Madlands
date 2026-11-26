"""End-to-end dry runs of the loop with a fake worktree, pipeline, builder and critic (a few seconds)."""

import copy
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from orchestrator import BLOCKED, IMPROVE, PASS, config, loop  # noqa: E402


class DryLoopTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="aicritic-test-"))
        self.cfg = copy.deepcopy(config.DEFAULTS)
        self.cfg["paths"]["runs_dir"] = str(self.tmp / "runs")
        self.cfg["paths"]["worktree_root"] = str(self.tmp / "worktrees")
        self.cfg["_source"] = "test"

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def run_loop(self, scenario, rounds=None):
        return loop.run(self.cfg, f"Dry run {scenario}", rounds, dry_run=True, fake_pipeline=True,
                        scenario=scenario, interactive=False)

    def run_dir(self, state):
        return Path(self.cfg["paths"]["runs_dir"]) / state["run_id"]

    def test_improves_then_passes_in_round_three(self):
        state = self.run_loop("improve-then-pass")
        self.assertEqual([r["verdict"] for r in state["rounds"]], [IMPROVE, IMPROVE, PASS])
        self.assertEqual(state["outcome"]["stop_reason"], "pass")
        run_dir = self.run_dir(state)
        for n in (1, 2, 3):
            rd = run_dir / f"round-{n:02d}"
            self.assertTrue((rd / "review.json").exists())
            self.assertTrue((rd / "review.md").exists())
            self.assertTrue((rd / "builder-prompt.md").exists())
            self.assertTrue((rd / "critic" / "critic-prompt.md").exists())
            self.assertTrue((rd / "evidence" / "manifest.json").exists())
        latest = json.loads((run_dir / "latest-review.json").read_text(encoding="utf-8"))
        self.assertEqual(latest["round"], 3)
        self.assertTrue((Path(self.cfg["paths"]["runs_dir"]) / "latest-review.md").exists())
        # Round 2 gets the critic's in-scope issue as feedback, not the out-of-scope one as a task.
        feedback = (run_dir / "round-02" / "builder-prompt.md").read_text(encoding="utf-8")
        self.assertIn("Dry-run issue", feedback)
        self.assertIn("Round 2 of 3", feedback)
        # The cumulative diff holds every round's change.
        self.assertEqual((run_dir / "round-03" / "diff.patch").read_text(encoding="utf-8").count("fake builder change"), 3)

    def test_stops_at_pass(self):
        state = self.run_loop("pass")
        self.assertEqual(len(state["rounds"]), 1)
        self.assertEqual(state["outcome"]["verdict"], PASS)

    def test_stops_after_max_rounds(self):
        state = self.run_loop("always-improve")
        self.assertEqual(len(state["rounds"]), 3)
        self.assertEqual(state["outcome"]["stop_reason"], "max_rounds")
        self.assertEqual(state["outcome"]["verdict"], IMPROVE)

    def test_rounds_option_limits_the_loop(self):
        state = self.run_loop("always-improve", rounds=1)
        self.assertEqual(len(state["rounds"]), 1)

    def test_null_criterion_blocks_and_stops(self):
        state = self.run_loop("blocked")
        self.assertEqual(len(state["rounds"]), 1)
        self.assertEqual(state["outcome"]["verdict"], BLOCKED)
        self.assertEqual(state["rounds"][0]["reason"], "insufficient_evidence")

    def test_failed_build_blocks_without_a_score(self):
        state = self.run_loop("build-fail")
        r = state["rounds"][0]
        self.assertEqual(r["verdict"], BLOCKED)
        self.assertEqual(r["reason"], "required_step_failed")
        self.assertIsNone(r["overall"])
        # The critic was never asked.
        self.assertFalse((self.run_dir(state) / "round-01" / "critic").exists())

    def test_critic_that_writes_is_rejected(self):
        state = self.run_loop("critic-writes")
        self.assertEqual(state["rounds"][0]["reason"], "critic_integrity_violation")
        self.assertEqual(state["outcome"]["verdict"], BLOCKED)

    def test_builder_failure_blocks(self):
        state = self.run_loop("builder-fail")
        self.assertEqual(state["rounds"][0]["reason"], "builder_failed")

    def test_budget_exhaustion_blocks(self):
        self.cfg["budget"]["builder_usd_total"] = 0.4
        state = self.run_loop("always-improve")
        self.assertEqual(state["outcome"]["stop_reason"], "builder_budget_exhausted")
        self.assertEqual(state["outcome"]["verdict"], BLOCKED)

    def test_dry_run_is_never_approvable(self):
        state = self.run_loop("pass")
        self.assertEqual(state["approval"]["state"], "dry_run")


if __name__ == "__main__":
    unittest.main()
