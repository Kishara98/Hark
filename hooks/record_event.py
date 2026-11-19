"""Hark hook recorder.

Claude Code runs this as an async command hook. It turns the hook's JSON
input into a small record and drops it into the Hark inbox, where the
Windows app picks it up. It never fails and never prints anything.
"""
import json
import os
import secrets
import sys
import time
from pathlib import Path

MAX_TEXT = 200
FILE_TOOLS = ("Read", "Write", "Edit", "MultiEdit", "NotebookEdit")
PATTERN_TOOLS = ("Grep", "Glob")
AGENT_TOOLS = ("Agent", "Task")


def _cut(text):
    text = " ".join(str(text).split())
    if len(text) > MAX_TEXT:
        return text[: MAX_TEXT - 3] + "..."
    return text


def _first_line(text):
    """The first line of a reply that reads as a sentence: no headings, list marks, bold or backticks."""
    lines = [line.strip() for line in str(text).splitlines() if line.strip()]
    prose = [line for line in lines if not line.startswith("#")]
    line = (prose or lines or [""])[0]
    line = line.lstrip("#>-*+ ").replace("**", "").replace("`", "")
    return line.strip()


def summarize_tool(tool, tool_input):
    tool_input = tool_input if isinstance(tool_input, dict) else {}
    if tool == "Bash" and tool_input.get("command"):
        return _cut(f"Bash: {tool_input['command']}")
    if tool in PATTERN_TOOLS and tool_input.get("pattern"):
        return _cut(f'{tool} "{tool_input["pattern"]}"')
    if tool in FILE_TOOLS:
        path = tool_input.get("file_path") or tool_input.get("notebook_path")
        if path:
            return _cut(f"{tool} {os.path.basename(str(path).rstrip('/'))}")
    if tool in AGENT_TOOLS and tool_input.get("description"):
        return _cut(f"{tool}: {tool_input['description']}")
    return _cut(tool or "")


def _questions(tool_input):
    result = []
    for q in (tool_input or {}).get("questions") or []:
        if not isinstance(q, dict):
            continue
        result.append({
            "question": _cut(q.get("question") or ""),
            "header": _cut(q.get("header") or ""),
            "options": [_cut(o.get("label") or "") for o in q.get("options") or [] if isinstance(o, dict)],
            "descriptions": [_cut(o.get("description") or "") for o in q.get("options") or [] if isinstance(o, dict)],
            "multi": bool(q.get("multiSelect")),
        })
    return result


def _todos(tool_input):
    result = []
    for t in (tool_input or {}).get("todos") or []:
        if not isinstance(t, dict):
            continue
        status = str(t.get("status") or "pending")
        text = t.get("activeForm") if status == "in_progress" and t.get("activeForm") else t.get("content")
        result.append({"text": _cut(text or ""), "status": status})
    return result


def _agent_task(payload):
    """The agent's description from the meta file Claude Code keeps next to its transcript, or None."""
    agent_id = payload.get("agent_id")
    transcript = payload.get("transcript_path")
    if not agent_id or not transcript:
        return None
    path = Path(transcript)
    if path.name == f"agent-{agent_id}.jsonl":
        meta = path.with_name(f"agent-{agent_id}.meta.json")
    else:
        meta = path.with_suffix("") / "subagents" / f"agent-{agent_id}.meta.json"
    try:
        description = json.loads(meta.read_text(encoding="utf-8")).get("description")
    except (OSError, ValueError, AttributeError):
        return None
    return _cut(description) if isinstance(description, str) and description.strip() else None


def build_record(payload, env, now_ms):
    cwd = payload.get("cwd") or ""
    tool = payload.get("tool_name")
    tool_input = payload.get("tool_input")
    record = {
        "ts": now_ms,
        "event": payload.get("hook_event_name"),
        "session": payload.get("session_id"),
        # The folder Claude started in; cwd moves when Claude runs cd.
        "project": os.path.basename((env.get("CLAUDE_PROJECT_DIR") or cwd).rstrip("/")),
        "cwd": cwd,
        "source": "vscode" if env.get("CLAUDE_CODE_ENTRYPOINT") == "claude-vscode" else "cli",
        "agent_id": payload.get("agent_id"),
        "agent_type": payload.get("agent_type"),
        "agent_task": _agent_task(payload),
        "tool": tool,
        "tool_use_id": payload.get("tool_use_id"),
        "summary": summarize_tool(tool, tool_input) if tool else None,
        "notification_type": payload.get("notification_type"),
        "message": _cut(payload["message"]) if payload.get("message") else None,
        "questions": _questions(tool_input) if tool == "AskUserQuestion" else None,
        "todos": _todos(tool_input) if tool == "TodoWrite" else None,
        # No subagent_type means Claude Code starts a general-purpose agent.
        "spawn_type": (tool_input.get("subagent_type") or "general-purpose") if tool in AGENT_TOOLS and isinstance(tool_input, dict) else None,
        "error": _cut(payload["error"]) if payload.get("error") else None,
        "last_message": _cut(_first_line(payload["last_assistant_message"])) if payload.get("last_assistant_message") else None,
    }
    return {k: v for k, v in record.items() if v not in (None, "", [])}


def app_alive(data_dir, now, max_age_s=120):
    try:
        return now - (Path(data_dir) / "heartbeat").stat().st_mtime <= max_age_s
    except OSError:
        return False


def write_event(record, inbox):
    inbox = Path(inbox)
    inbox.mkdir(parents=True, exist_ok=True)
    name = f"{record.get('ts', 0)}-{os.getpid()}-{secrets.token_hex(3)}.json"
    tmp = inbox / (name + ".tmp")
    tmp.write_text(json.dumps(record, ensure_ascii=False), encoding="utf-8")
    final = inbox / name
    os.replace(tmp, final)
    return final


def main(argv, stdin, env, clock=time.time):
    # Stamp first: async hooks start a few ms apart, and the order of events matters to Hark.
    now_ms = int(clock() * 1000)
    try:
        raw = stdin.read()
        if "--data-dir" not in argv:
            return 0
        if str(env.get("CLAUDE_CODE_ENTRYPOINT", "")).startswith("sdk"):
            return 0  # scripted runs (claude -p, Agent SDK): nobody is there to answer
        data_dir = Path(argv[argv.index("--data-dir") + 1])
        if not app_alive(data_dir, clock()):
            return 0
        payload = json.loads(raw)
        if not isinstance(payload, dict):
            return 0
        write_event(build_record(payload, env, now_ms), data_dir / "inbox")
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:], sys.stdin, os.environ))
