"""Validates the critic's review and turns it into a verdict.

The critic scores each criterion (0-10) or marks it not evaluable (null). The orchestrator, never the
critic, computes the weighted score:
  - any criterion null            -> BLOCKED (no defensible overall score)
  - all scored, score >= pass     -> PASS
  - all scored, score <  pass     -> IMPROVE
"""

from . import BLOCKED, CRITERIA, IMPROVE, PASS

SEVERITIES = ("critical", "major", "minor")
CONFIDENCES = ("low", "medium", "high")
TASK_STATES = ("yes", "partly", "no", "unknown")


class ReviewError(Exception):
    pass


def _need(cond, msg):
    if not cond:
        raise ReviewError(msg)


def validate_review(review, pass_score=None):
    """Raises ReviewError when the review breaks the contract; returns a list of warnings."""
    warnings = []
    _need(isinstance(review, dict), "review is not a JSON object")
    for key in ("summary", "task_assessment", "criteria", "issues"):
        _need(key in review, f"review has no '{key}'")
    _need(isinstance(review["summary"], str), "summary must be a string")
    ta = review["task_assessment"]
    _need(isinstance(ta, dict) and ta.get("completed") in TASK_STATES, "task_assessment.completed is invalid")
    crit = review["criteria"]
    _need(isinstance(crit, dict), "criteria must be an object")
    _need(set(crit) == set(CRITERIA), f"criteria must be exactly {', '.join(CRITERIA)}")
    for name in CRITERIA:
        c = crit[name]
        _need(isinstance(c, dict), f"{name} must be an object")
        _need(isinstance(c.get("evaluable"), bool), f"{name}.evaluable must be a boolean")
        score = c.get("score")
        if c["evaluable"]:
            _need(isinstance(score, (int, float)) and not isinstance(score, bool),
                  f"{name} is evaluable but has no numeric score")
            _need(0 <= score <= 10, f"{name}.score {score} is outside 0..10")
            if not c.get("evidence"):
                warnings.append(f"{name} has a score but cites no evidence")
        else:
            _need(score is None, f"{name} is not evaluable, so its score must be null (got {score!r})")
            if not c.get("missing_evidence"):
                warnings.append(f"{name} is not evaluable but does not say which evidence is missing")
        _need(c.get("confidence") in CONFIDENCES, f"{name}.confidence is invalid")
        _need(isinstance(c.get("evidence", []), list), f"{name}.evidence must be a list")
    issues = review["issues"]
    _need(isinstance(issues, list), "issues must be a list")
    for i, issue in enumerate(issues):
        _need(isinstance(issue, dict), f"issue {i} is not an object")
        _need(issue.get("severity") in SEVERITIES, f"issue {i} has an invalid severity")
        _need(issue.get("criterion") in CRITERIA, f"issue {i} names an unknown criterion")
        _need(isinstance(issue.get("in_scope"), bool), f"issue {i}.in_scope must be a boolean")
        _need(isinstance(issue.get("rank"), int), f"issue {i}.rank must be an integer")
    if pass_score is not None:
        explained = {i["criterion"] for i in issues}
        for name in CRITERIA:
            c = crit[name]
            if c["evaluable"] and c["score"] < pass_score and name not in explained:
                warnings.append(f"{name} scored {c['score']} (below {pass_score}) without an issue explaining it")
    return warnings


MAX_ISSUES = 5


def normalize_review(review, pass_score):
    """Validates the review and keeps at most MAX_ISSUES issues, the most important first.
    Returns (review, warnings)."""
    warnings = validate_review(review, pass_score)
    issues = ranked_issues(review)
    if len(issues) > MAX_ISSUES:
        warnings.append(f"the critic listed {len(issues)} issues; only the {MAX_ISSUES} most important are kept")
    review = dict(review, issues=issues[:MAX_ISSUES])
    return review, warnings


def score(review, weights, pass_score):
    """The verdict for a valid review."""
    crit = review["criteria"]
    evaluated = [n for n in CRITERIA if crit[n]["evaluable"]]
    missing = [n for n in CRITERIA if not crit[n]["evaluable"]]
    covered = sum(weights[n] for n in evaluated)
    partial = (sum(weights[n] * crit[n]["score"] for n in evaluated) / covered) if covered else None
    per = {n: {"score": crit[n]["score"], "weight": weights[n], "evaluable": crit[n]["evaluable"],
               "confidence": crit[n]["confidence"]} for n in CRITERIA}
    if missing:
        return {
            "verdict": BLOCKED,
            "reason": "insufficient_evidence",
            "detail": "not evaluable: " + ", ".join(missing),
            "overall": None,
            "indicative_partial_score": round(partial, 2) if partial is not None else None,
            "coverage_percent": round(covered, 1),
            "criteria": per,
            "pass_score": pass_score,
        }
    overall = round(partial, 2)
    return {
        "verdict": PASS if overall >= pass_score else IMPROVE,
        "reason": "score_reached" if overall >= pass_score else "below_threshold",
        "detail": f"weighted score {overall} (pass needs {pass_score})",
        "overall": overall,
        "indicative_partial_score": None,
        "coverage_percent": 100.0,
        "criteria": per,
        "pass_score": pass_score,
    }


def blocked(reason, detail):
    """The verdict for a round that never reached a valid review."""
    return {"verdict": BLOCKED, "reason": reason, "detail": detail, "overall": None,
            "indicative_partial_score": None, "coverage_percent": 0.0, "criteria": None, "pass_score": None}


def ranked_issues(review, in_scope_only=False):
    issues = sorted(review.get("issues", []), key=lambda i: (i.get("rank", 999), SEVERITIES.index(i["severity"])))
    return [i for i in issues if i["in_scope"]] if in_scope_only else issues
