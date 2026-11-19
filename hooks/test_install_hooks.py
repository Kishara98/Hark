import copy
import unittest

from install_hooks import EVENTS, MARKER, merge_hooks, remove_hooks

CMD = "python3 -I /home/dev/.local/share/Hark/record_event.py --data-dir /mnt/c/Users/x/AppData/Local/Hark"
OTHER = {"hooks": [{"type": "command", "command": "/opt/plugin/session-start.sh"}]}


class MergeTests(unittest.TestCase):
    def test_marker_matches_command(self):
        self.assertIn(MARKER, CMD)

    def test_merge_into_empty(self):
        s = merge_hooks({}, CMD)
        self.assertEqual(sorted(s["hooks"]), sorted(EVENTS))
        self.assertEqual(s["hooks"]["Stop"], [{"hooks": [{"type": "command", "command": CMD, "async": True}]}])

    def test_merge_keeps_other_hooks(self):
        s = merge_hooks({"model": "x", "hooks": {"SessionStart": [copy.deepcopy(OTHER)]}}, CMD)
        self.assertEqual(s["model"], "x")
        self.assertEqual(s["hooks"]["SessionStart"][0], OTHER)
        self.assertEqual(s["hooks"]["SessionStart"][1]["hooks"][0]["command"], CMD)

    def test_merge_does_not_change_input(self):
        original = {"hooks": {"SessionStart": [copy.deepcopy(OTHER)]}}
        merge_hooks(original, CMD)
        self.assertEqual(original, {"hooks": {"SessionStart": [OTHER]}})

    def test_merge_twice_is_same(self):
        once = merge_hooks({"hooks": {"SessionStart": [copy.deepcopy(OTHER)]}}, CMD)
        self.assertEqual(merge_hooks(once, CMD), once)

    def test_merge_replaces_old_command(self):
        once = merge_hooks({}, CMD + "-old")
        twice = merge_hooks(once, CMD)
        self.assertEqual(twice["hooks"]["Stop"], [{"hooks": [{"type": "command", "command": CMD, "async": True}]}])


ANSWER = "python3 -I /home/dev/.local/share/Hark/answer_hook.py --data-dir /mnt/c/Users/x/AppData/Local/Hark"


class AnswerHookEntryTests(unittest.TestCase):
    def test_answer_hook_waits_only_for_questions(self):
        s = merge_hooks({}, CMD, ANSWER)
        entry = s["hooks"]["PreToolUse"][-1]
        self.assertEqual(entry["matcher"], "AskUserQuestion")
        self.assertEqual(entry["hooks"], [{"type": "command", "command": ANSWER, "timeout": 1860}])

    def test_answer_hook_merge_twice_is_same(self):
        once = merge_hooks({}, CMD, ANSWER)
        self.assertEqual(merge_hooks(once, CMD, ANSWER), once)

    def test_remove_drops_answer_hook_too(self):
        self.assertEqual(remove_hooks(merge_hooks({"model": "x"}, CMD, ANSWER)), {"model": "x"})


AGENT = "python3 -I /home/dev/.local/share/Hark/agent_hook.py --data-dir /mnt/c/Users/x/AppData/Local/Hark"


class AgentHookEntryTests(unittest.TestCase):
    def test_agent_hook_runs_when_an_agent_starts(self):
        s = merge_hooks({}, CMD, ANSWER, AGENT)
        entry = s["hooks"]["SubagentStart"][-1]
        self.assertEqual(entry["hooks"], [{"type": "command", "command": AGENT, "timeout": 10}])

    def test_agent_hook_also_runs_on_each_prompt(self):
        s = merge_hooks({}, CMD, ANSWER, AGENT)
        self.assertEqual(s["hooks"]["UserPromptSubmit"][-1]["hooks"], [{"type": "command", "command": AGENT, "timeout": 10}])

    def test_remove_drops_agent_hook_too(self):
        self.assertEqual(remove_hooks(merge_hooks({"model": "x"}, CMD, ANSWER, AGENT)), {"model": "x"})


class RemoveTests(unittest.TestCase):
    def test_remove_only_ours(self):
        s = merge_hooks({"hooks": {"SessionStart": [copy.deepcopy(OTHER)]}}, CMD)
        r = remove_hooks(s)
        self.assertEqual(r, {"hooks": {"SessionStart": [OTHER]}})

    def test_remove_drops_empty_hooks_key(self):
        self.assertEqual(remove_hooks(merge_hooks({"model": "x"}, CMD)), {"model": "x"})

    def test_remove_on_settings_without_hooks(self):
        self.assertEqual(remove_hooks({"model": "x"}), {"model": "x"})


if __name__ == "__main__":
    unittest.main()
