"""Hark answer hook.

Claude Code runs this as a synchronous PreToolUse hook for AskUserQuestion. While Hark is running and not
paused, it holds the question (Claude Code waits) and lets the Hark card answer it:
- Hark writes answers/<tool_use_id>.json with {"action": "answer", "answers": {question: answer}}: the hook
  returns them, so Claude Code skips its own dialog.
- {"action": "vscode"}, 30 minutes without an answer, or Hark stopping: the hook prints nothing and the
  normal question dialog shows.
It never fails and prints only the answer JSON.
"""
import json
import os
import re
import sys
import time
from pathlib import Path

HOLD_S = 1800
POLL_S = 0.25
ALIVE_S = 120
SAFE_ID = re.compile(r"[A-Za-z0-9_-]{1,128}")
MAX_TEXT = 200


def _cut(text):
    """Same as record_event._cut: Hark only ever sees question and label text in this form."""
    text = " ".join(str(text).split())
    if len(text) > MAX_TEXT:
        return text[: MAX_TEXT - 3] + "..."
    return text


def _restore(answers, tool_input):
    """Keys back to the original question text, shortened labels back to the original labels."""
    questions = tool_input.get("questions") if isinstance(tool_input, dict) else None
    if not isinstance(questions, list):
        return answers
    result = {}
    for q in questions:
        if not isinstance(q, dict) or not q.get("question"):
            continue
        text = q["question"]
        value = answers.get(text, answers.get(_cut(text)))
        if value is None:
            continue
        labels = {_cut(o["label"]): o["label"] for o in q.get("options") or [] if isinstance(o, dict) and o.get("label")}
        if value in labels:
            value = labels[value]
        elif q.get("multiSelect") and all(part in labels for part in value.split(", ")):
            value = ", ".join(labels[part] for part in value.split(", "))
        result[text] = value
    return result or answers


def _alive(data_dir, now):
    try:
        return now - (data_dir / "heartbeat").stat().st_mtime <= ALIVE_S
    except OSError:
        return False


def _write_atomic(path, body):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    tmp.write_text(json.dumps(body), encoding="utf-8")
    os.replace(tmp, path)


def _read_answer(path):
    try:
        body = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None
    try:
        path.unlink()
    except OSError:
        pass
    return body if isinstance(body, dict) else {}


def wait_for_answer(data_dir, tool_use_id, clock, sleep):
    path = data_dir / "answers" / f"{tool_use_id}.json"
    deadline = clock() + HOLD_S
    while True:
        if path.exists():
            body = _read_answer(path)
            if body is not None:
                return body
        if clock() >= deadline or not _alive(data_dir, clock()):
            return None
        sleep(POLL_S)


def decide(answer, tool_input):
    if not answer or answer.get("action") != "answer":
        return None
    answers = answer.get("answers")
    if not isinstance(answers, dict) or not answers:
        return None
    if not all(isinstance(k, str) and isinstance(v, str) and v for k, v in answers.items()):
        return None
    answers = _restore(answers, tool_input)
    return {
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "allow",
            "updatedInput": {**tool_input, "answers": answers},
        }
    }


def main(argv, stdin, env, stdout, clock=time.time, sleep=time.sleep):
    try:
        payload = json.loads(stdin.read())
        if not isinstance(payload, dict) or "--data-dir" not in argv:
            return 0
        if str(env.get("CLAUDE_CODE_ENTRYPOINT", "")).startswith("sdk"):
            return 0
        if payload.get("tool_name") != "AskUserQuestion" or payload.get("agent_id"):
            return 0
        tool_use_id = str(payload.get("tool_use_id") or "")
        if not SAFE_ID.fullmatch(tool_use_id):
            return 0
        data_dir = Path(argv[argv.index("--data-dir") + 1])
        if not _alive(data_dir, clock()) or (data_dir / "paused").exists():
            return 0
        held = data_dir / "held" / f"{tool_use_id}.json"
        _write_atomic(held, {"session": payload.get("session_id"), "ts": int(clock() * 1000)})
        try:
            answer = wait_for_answer(data_dir, tool_use_id, clock, sleep)
        finally:
            try:
                held.unlink()
            except OSError:
                pass
        tool_input = payload.get("tool_input") if isinstance(payload.get("tool_input"), dict) else {}
        out = decide(answer, tool_input)
        if out:
            stdout.write(json.dumps(out))
            stdout.flush()
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:], sys.stdin, os.environ, sys.stdout))
