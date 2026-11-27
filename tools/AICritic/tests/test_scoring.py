import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from orchestrator import BLOCKED, CRITERIA, IMPROVE, PASS, config, fakes, scoring  # noqa: E402

WEIGHTS = config.DEFAULTS["weights"]


def review_with(scores):
    """scores: {criterion: number or None}; missing criteria get 8."""
    r = fakes.review(1, "pass")
    for n in CRITERIA:
        s = scores.get(n, 8.0)
        r["criteria"][n]["evaluable"] = s is not None
        r["criteria"][n]["score"] = s
        r["criteria"][n]["missing_evidence"] = "" if s is not None else "no data"
    return r


class ScoringTests(unittest.TestCase):
    def test_weights_add_up(self):
        self.assertEqual(sum(WEIGHTS.values()), 100)

    def test_pass_at_threshold(self):
        v = scoring.score(review_with({}), WEIGHTS, 8.0)
        self.assertEqual(v["verdict"], PASS)
        self.assertEqual(v["overall"], 8.0)

    def test_weighted_average(self):
        # 25*10 + 30*6 + 20*8 + 15*8 + 10*8 = 790 -> 7.9
        v = scoring.score(review_with({"visual_quality": 10, "gameplay": 6}), WEIGHTS, 8.0)
        self.assertEqual(v["verdict"], IMPROVE)
        self.assertAlmostEqual(v["overall"], 7.9)

    def test_any_null_blocks_even_with_high_scores(self):
        v = scoring.score(review_with({n: 10.0 for n in CRITERIA} | {"performance": None}), WEIGHTS, 8.0)
        self.assertEqual(v["verdict"], BLOCKED)
        self.assertEqual(v["reason"], "insufficient_evidence")
        self.assertIsNone(v["overall"])
        self.assertEqual(v["coverage_percent"], 90.0)
        self.assertEqual(v["indicative_partial_score"], 10.0)

    def test_gameplay_null_blocks(self):
        v = scoring.score(review_with({"gameplay": None}), WEIGHTS, 8.0)
        self.assertEqual(v["verdict"], BLOCKED)

    def test_validation_rejects_score_on_unevaluable(self):
        r = review_with({})
        r["criteria"]["performance"]["evaluable"] = False
        with self.assertRaises(scoring.ReviewError):
            scoring.validate_review(r)

    def test_validation_rejects_missing_score(self):
        r = review_with({})
        r["criteria"]["gameplay"]["score"] = None
        with self.assertRaises(scoring.ReviewError):
            scoring.validate_review(r)

    def test_validation_rejects_out_of_range(self):
        r = review_with({"gameplay": 11})
        with self.assertRaises(scoring.ReviewError):
            scoring.validate_review(r)

    def test_validation_rejects_missing_criterion(self):
        r = review_with({})
        del r["criteria"]["performance"]
        with self.assertRaises(scoring.ReviewError):
            scoring.validate_review(r)

    def test_validation_rejects_bool_score(self):
        r = review_with({})
        r["criteria"]["gameplay"]["score"] = True
        with self.assertRaises(scoring.ReviewError):
            scoring.validate_review(r)

    def test_warns_about_unexplained_low_scores(self):
        r = review_with({"gameplay": 5.0, "visual_quality": 6.0})  # the fixture's only issue is visual_quality
        warnings = scoring.validate_review(r, 8.0)
        self.assertEqual(len(warnings), 1)
        self.assertIn("gameplay", warnings[0])

    def test_ranked_issues(self):
        r = fakes.review(1, "pass")
        r["issues"].reverse()
        self.assertEqual([i["rank"] for i in scoring.ranked_issues(r)], [1, 2])
        self.assertEqual(len(scoring.ranked_issues(r, in_scope_only=True)), 1)


class CaptureRetryTests(unittest.TestCase):
    def test_only_socket_failures_before_login_are_retried(self):
        from orchestrator import capture
        bind = {"client_log": {"entered_world": False, "first_problems": [
            "[B]Bind exception: System.Net.Sockets.SocketException (0x80004005): ... lacked sufficient buffer space"]}}
        game = {"client_log": {"entered_world": False, "first_problems": ["NullReferenceException in HudController"]}}
        late = {"client_log": {"entered_world": True, "first_problems": ["SocketException"]}}
        self.assertTrue(capture.infrastructure_failure(bind))
        self.assertFalse(capture.infrastructure_failure(game))
        self.assertFalse(capture.infrastructure_failure(late))


class ConfigTests(unittest.TestCase):
    def test_defaults_valid(self):
        config.validate(copy.deepcopy(config.DEFAULTS))

    def test_rounds_capped(self):
        cfg = copy.deepcopy(config.DEFAULTS)
        cfg["loop"]["max_rounds"] = 4
        with self.assertRaises(config.ConfigError):
            config.validate(cfg)

    def test_weights_must_sum_to_100(self):
        cfg = copy.deepcopy(config.DEFAULTS)
        cfg["weights"]["performance"] = 20
        with self.assertRaises(config.ConfigError):
            config.validate(cfg)

    def test_scene_loads(self):
        scene = config.load_scene(copy.deepcopy(config.DEFAULTS))
        self.assertGreaterEqual(len(scene["shot"]), 4)


if __name__ == "__main__":
    unittest.main()
