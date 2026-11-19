import io
import json
import os
import tempfile
import time
import unittest
from pathlib import Path

from agent_hook import PLAN_HINT, TASK_HINT, main

START = {"session_id": "s1", "cwd": "/home/dev/projects", "hook_event_name": "SubagentStart", "agent_id": "agent-1", "agent_type": "general-purpose"}


class AgentHookTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.data = Path(self.tmp.name)
        (self.data / "heartbeat").touch()

    def tearDown(self):
        self.tmp.cleanup()

    def run_hook(self, payload=START, env=None):
        out = io.StringIO()
        self.assertEqual(main(["--data-dir", str(self.data)], io.StringIO(json.dumps(payload)), env or {}, out), 0)
        return out.getvalue()

    def test_asks_the_agent_for_a_todo_list(self):
        out = json.loads(self.run_hook())
        hso = out["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "SubagentStart")
        self.assertEqual(hso["additionalContext"], PLAN_HINT)
        self.assertIn("TodoWrite", PLAN_HINT)

    def test_asks_the_main_session_for_a_todo_list_on_each_prompt(self):
        prompt = {"session_id": "s1", "cwd": "/home/dev/projects", "hook_event_name": "UserPromptSubmit", "prompt": "do it"}
        hso = json.loads(self.run_hook(prompt))["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "UserPromptSubmit")
        self.assertEqual(hso["additionalContext"], TASK_HINT)
        self.assertIn("several steps", TASK_HINT)

    def test_quiet_when_hark_is_not_running(self):
        old = time.time() - 300
        os.utime(self.data / "heartbeat", (old, old))
        self.assertEqual(self.run_hook(), "")

    def test_quiet_when_paused(self):
        (self.data / "paused").touch()
        self.assertEqual(self.run_hook(), "")

    def test_quiet_for_scripted_runs_and_other_events(self):
        self.assertEqual(self.run_hook(env={"CLAUDE_CODE_ENTRYPOINT": "sdk-cli"}), "")
        self.assertEqual(self.run_hook({**START, "hook_event_name": "Stop"}), "")

    def test_bad_stdin_prints_nothing(self):
        out = io.StringIO()
        self.assertEqual(main(["--data-dir", str(self.data)], io.StringIO("nope"), {}, out), 0)
        self.assertEqual(out.getvalue(), "")


if __name__ == "__main__":
    unittest.main()
