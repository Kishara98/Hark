"""Hark agent hook.

Claude Code runs this as a SubagentStart and a UserPromptSubmit hook. While Hark runs and is not paused, it asks
the new agent, or the main session for a multi-step request, to keep a short to-do list, so Hark can show real
progress (done items out of all items). It never fails and
prints only that JSON.
"""
import json
import os
import sys
import time
from pathlib import Path

ALIVE_S = 120
PLAN_HINT = (
    "Hark, the user's progress viewer, shows your progress. If you have the TodoWrite tool, start by writing "
    "a short to-do list for this task with it (3 to 7 items) and keep it updated: mark the item you work on "
    "as in_progress and finished items as completed. If you do not have TodoWrite, ignore this note."
)
TASK_HINT = (
    "Hark, the user's progress viewer, shows your progress. When this request has several steps, keep a short "
    "TodoWrite to-do list for it (3 to 7 items) and keep it updated as you work. For a quick single-step request, "
    "skip the list."
)
HINTS = {"SubagentStart": PLAN_HINT, "UserPromptSubmit": TASK_HINT}


def _alive(data_dir, now):
    try:
        return now - (data_dir / "heartbeat").stat().st_mtime <= ALIVE_S
    except OSError:
        return False


def main(argv, stdin, env, stdout, clock=time.time):
    try:
        payload = json.loads(stdin.read())
        if not isinstance(payload, dict) or "--data-dir" not in argv:
            return 0
        if str(env.get("CLAUDE_CODE_ENTRYPOINT", "")).startswith("sdk"):
            return 0
        event = payload.get("hook_event_name")
        if event not in HINTS:
            return 0
        data_dir = Path(argv[argv.index("--data-dir") + 1])
        if not _alive(data_dir, clock()) or (data_dir / "paused").exists():
            return 0
        stdout.write(json.dumps({"hookSpecificOutput": {"hookEventName": event, "additionalContext": HINTS[event]}}))
        stdout.flush()
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:], sys.stdin, os.environ, sys.stdout))
