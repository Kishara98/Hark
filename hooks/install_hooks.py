"""Install or remove the Hark hooks in ~/.claude/settings.json.

Usage: python3 -I hooks/install_hooks.py install|uninstall
"""
import copy
import json
import shlex
import shutil
import subprocess
import sys
import time
from pathlib import Path

EVENTS = [
    "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure",
    "PermissionRequest", "Notification", "SubagentStart", "SubagentStop", "Stop",
    "StopFailure", "SessionEnd",
]
MARKER = "/Hark/record_event.py"
ANSWER_MARKER = "/Hark/answer_hook.py"
AGENT_MARKER = "/Hark/agent_hook.py"
ANSWER_TIMEOUT_S = 1860  # the answer hook holds a question up to 30 minutes

SETTINGS = Path.home() / ".claude" / "settings.json"
HOOKS_DEST = Path.home() / ".local" / "share" / "Hark"
RECORDER_DEST = HOOKS_DEST / "record_event.py"
ANSWER_DEST = HOOKS_DEST / "answer_hook.py"
AGENT_DEST = HOOKS_DEST / "agent_hook.py"


def _is_ours(hook):
    command = str(hook.get("command", "")) if isinstance(hook, dict) else ""
    return MARKER in command or ANSWER_MARKER in command or AGENT_MARKER in command


def remove_hooks(settings):
    result = copy.deepcopy(settings)
    hooks = result.get("hooks")
    if not isinstance(hooks, dict):
        return result
    for event in list(hooks):
        groups = []
        for group in hooks[event] or []:
            kept = [h for h in group.get("hooks", []) if not _is_ours(h)]
            if kept:
                groups.append({**group, "hooks": kept})
        if groups:
            hooks[event] = groups
        else:
            del hooks[event]
    if not hooks:
        del result["hooks"]
    return result


def merge_hooks(settings, command, answer_command=None, agent_command=None):
    result = remove_hooks(settings)
    hooks = result.setdefault("hooks", {})
    for event in EVENTS:
        hooks.setdefault(event, []).append({"hooks": [{"type": "command", "command": command, "async": True}]})
    if answer_command:
        hooks["PreToolUse"].append({
            "matcher": "AskUserQuestion",
            "hooks": [{"type": "command", "command": answer_command, "timeout": ANSWER_TIMEOUT_S}],
        })
    if agent_command:
        hooks["SubagentStart"].append({"hooks": [{"type": "command", "command": agent_command, "timeout": 10}]})
        hooks["UserPromptSubmit"].append({"hooks": [{"type": "command", "command": agent_command, "timeout": 10}]})
    return result


def _windows_data_dir():
    win = subprocess.run(["cmd.exe", "/c", "echo %LOCALAPPDATA%"], cwd="/mnt/c", capture_output=True, text=True, check=True).stdout.strip()
    unix = subprocess.run(["wslpath", "-u", win], capture_output=True, text=True, check=True).stdout.strip()
    return Path(unix) / "Hark"


def _load():
    return json.loads(SETTINGS.read_text(encoding="utf-8")) if SETTINGS.exists() else {}


def _save(settings):
    if SETTINGS.exists():
        backup = SETTINGS.with_name(f"settings.json.bak-{time.strftime('%Y%m%d-%H%M%S')}")
        shutil.copy2(SETTINGS, backup)
        print(f"backup: {backup}")
    SETTINGS.parent.mkdir(parents=True, exist_ok=True)
    SETTINGS.write_text(json.dumps(settings, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def install():
    HOOKS_DEST.mkdir(parents=True, exist_ok=True)
    for dest in (RECORDER_DEST, ANSWER_DEST, AGENT_DEST):
        tmp = dest.with_name(dest.name + ".tmp")
        shutil.copy2(Path(__file__).with_name(dest.name), tmp)
        tmp.replace(dest)
    data_dir = _windows_data_dir()
    (data_dir / "inbox").mkdir(parents=True, exist_ok=True)
    data = shlex.quote(str(data_dir))
    command = f"python3 -I {shlex.quote(str(RECORDER_DEST))} --data-dir {data}"
    answer_command = f"python3 -I {shlex.quote(str(ANSWER_DEST))} --data-dir {data}"
    agent_command = f"python3 -I {shlex.quote(str(AGENT_DEST))} --data-dir {data}"
    _save(merge_hooks(_load(), command, answer_command, agent_command))
    print(f"installed {len(EVENTS)} event hooks: {command}")
    print(f"installed the answer hook: {answer_command}")
    print(f"installed the agent hook: {agent_command}")


def uninstall():
    _save(remove_hooks(_load()))
    for dest in (RECORDER_DEST, ANSWER_DEST, AGENT_DEST):
        if dest.exists():
            dest.unlink()
    print("removed the Hark hooks")


if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "install":
        install()
    elif action == "uninstall":
        uninstall()
    else:
        print(__doc__)
        sys.exit(2)
