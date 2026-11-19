#!/usr/bin/env bash
# Drop sample events into the Hark inbox so the pill and cards can be checked by eye.
# Usage: scripts/demo.sh question|permission|agents|done|clear|all
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/env.sh
touch "$HARK_DATA/heartbeat"
python3 -I - "$1" tests/fixtures/events "$HARK_DATA/inbox" <<'EOF'
import json, os, sys, time
scenario, fixtures, inbox = sys.argv[1], sys.argv[2], sys.argv[3]
CLI = "0c1d2e3f-4a5b-4c6d-8e7f-a0b1c2d3e4f5"

def load(name, **changes):
    with open(os.path.join(fixtures, name), encoding="utf-8") as f:
        record = json.load(f)
    record.update(changes)
    return record

steps = {
    "question": [load("ask_pre.json")],
    "permission": [load("permission_request.json")],
    "agents": [load("subagent_start.json", agent_id="demo-a1"), load("subagent_start.json", agent_id="demo-a2")],
    "done": [
        load("subagent_start.json", session="s-notes", project="Notes", agent_id="demo-a3"),
        load("subagent_stop.json", session="s-notes", project="Notes", agent_id="demo-a3"),
        load("subagent_start.json", session="s-notes", project="Notes", agent_id="demo-a4"),
        load("subagent_stop.json", session="s-notes", project="Notes", agent_id="demo-a4"),
        load("stop.json", session="s-notes", project="Notes", last_message="Morning report sent."),
    ],
}
MULTI = [
    {"question": "Which parent branch?", "header": "Parent", "options": ["main", "release/2.0-rc"],
     "descriptions": ["Default branch", "Current release candidate"], "multi": False},
    {"question": "Which checks should run?", "header": "Checks", "options": ["Unit", "Lint", "E2E"],
     "descriptions": ["Fast", "Style", "Slow, needs the test tenant"], "multi": True},
    {"question": "Push when done?", "header": "Push", "options": ["Yes", "No"], "descriptions": ["", ""], "multi": False},
]
steps["questions"] = [load("ask_pre.json", tool_use_id="toolu_ask3", questions=MULTI)]
def main_event(event, **fields):
    return {"event": event, "session": CLI, "project": "projects", "cwd": "/home/dev/projects", "source": "vscode", **fields}

PANEL = []
for agent_id, kind, task in (("demo-p1", "Explore", "Find split logic in the importer"),
                             ("demo-p2", "general-purpose", "Check release/2.0-rc for PROJ-2070"),
                             ("demo-p3", "Plan", "Draft the hook setup")):
    PANEL.append(main_event("PreToolUse", tool="Agent", spawn_type=kind, summary="Agent: " + task))
    PANEL.append(main_event("SubagentStart", agent_id=agent_id, agent_type=kind))
for n, step in enumerate(["Grep \"pageSize\"", "Read SplitConfig.java", "Read Splitter.java", "Grep \"DEFAULT_PAGE_SIZE\"", "Read Worker.java", "Grep \"split\"", "Read Mode.java"]):
    PANEL.append(main_event("PreToolUse", agent_id="demo-p1", agent_type="Explore", tool="Grep", summary=step))
PANEL.append(main_event("PreToolUse", agent_id="demo-p2", agent_type="general-purpose", tool="TodoWrite", summary="TodoWrite", todos=[
    {"text": "Fetch the release branch", "status": "completed"}, {"text": "Compare its commits with the ticket", "status": "completed"},
    {"text": "Find the split mode changes", "status": "completed"}, {"text": "Running the importer tests", "status": "in_progress"},
    {"text": "Write a short summary", "status": "pending"}]))
for step in ("Bash: git fetch origin", "Read SplitConfig.java", "Bash: mvn -q test"):
    PANEL.append(main_event("PreToolUse", agent_id="demo-p2", agent_type="general-purpose", tool="Bash", summary=step))
for step in ("Read settings.json", "Read install_hooks.py", "Grep \"SubagentStart\""):
    PANEL.append(main_event("PreToolUse", agent_id="demo-p3", agent_type="Plan", tool="Read", summary=step))
steps["agentpanel"] = PANEL
steps["all"] = steps["question"] + steps["permission"] + steps["agents"] + steps["done"]
steps["clear"] = [
    load("session_end.json", session=s)
    for s in ("s-webshop", CLI, "s-notes", load("permission_request.json")["session"])
]
if scenario not in steps:
    sys.exit("usage: demo.sh " + "|".join(steps))
os.makedirs(inbox, exist_ok=True)
ts = int(time.time() * 1000)
for n, record in enumerate(steps[scenario]):
    record["ts"] = ts + n
    name = f"{record['ts']}-demo-{n}.json"
    with open(os.path.join(inbox, name + ".tmp"), "w", encoding="utf-8") as f:
        json.dump(record, f)
    os.replace(os.path.join(inbox, name + ".tmp"), os.path.join(inbox, name))
print(f"{scenario}: {len(steps[scenario])} events")
EOF
