"""AI Builder/Critic orchestrator for Madlands (see tools/AICritic/README.md)."""

CRITERIA = ("visual_quality", "gameplay", "animation_physics", "bugs_stability", "performance")

CRITERION_TITLES = {
    "visual_quality": "Visual quality and art style",
    "gameplay": "Gameplay",
    "animation_physics": "Animation and physics",
    "bugs_stability": "Bugs and stability",
    "performance": "Performance",
}

# Round verdicts. PASS: every criterion scored and the weighted score reaches the threshold.
# IMPROVE: every criterion scored, below the threshold. BLOCKED: the evidence is insufficient,
# a required step failed, or the critic could not produce a trustworthy review.
PASS, IMPROVE, BLOCKED = "PASS", "IMPROVE", "BLOCKED"

# The loop never runs more evaluation rounds than this, whatever the config says.
HARD_MAX_ROUNDS = 3
