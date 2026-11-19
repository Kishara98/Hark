import io
import json
import os
import tempfile
import time
import unittest
from pathlib import Path

import answer_hook
import record_event
from answer_hook import main

QUESTIONS = [
    {
        "question": "Which parent branch?",
        "header": "Parent",
        "multiSelect": False,
        "options": [{"label": "main", "description": "Main"}, {"label": "release/2.0-rc", "description": "Release"}],
    }
]
PAYLOAD = {
    "session_id": "s1",
    "cwd": "/home/dev/projects/WebShop",
    "hook_event_name": "PreToolUse",
    "tool_name": "AskUserQuestion",
    "tool_use_id": "toolu_ask1",
    "tool_input": {"questions": QUESTIONS},
}


class FakeTime:
    """A clock that only moves when the hook sleeps."""

    def __init__(self, on_sleep=None):
        self.now = time.time()
        self.on_sleep = on_sleep

    def clock(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds
        if self.on_sleep:
            self.on_sleep(self)


class AnswerHookTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.data = Path(self.tmp.name)
        (self.data / "heartbeat").touch()
        self.answers = self.data / "answers"
        self.held = self.data / "held"

    def tearDown(self):
        self.tmp.cleanup()

    def run_hook(self, payload=PAYLOAD, env=None, fake=None):
        out = io.StringIO()
        fake = fake or FakeTime()
        rc = main(["--data-dir", str(self.data)], io.StringIO(json.dumps(payload)), env or {}, out, clock=fake.clock, sleep=fake.sleep)
        self.assertEqual(rc, 0)
        return out.getvalue()

    def answer(self, body):
        self.answers.mkdir(exist_ok=True)
        (self.answers / "toolu_ask1.json").write_text(json.dumps(body), encoding="utf-8")

    def test_answer_returns_allow_with_answers(self):
        self.answer({"action": "answer", "answers": {"Which parent branch?": "main"}})
        out = json.loads(self.run_hook())
        hso = out["hookSpecificOutput"]
        self.assertEqual(hso["hookEventName"], "PreToolUse")
        self.assertEqual(hso["permissionDecision"], "allow")
        self.assertEqual(hso["updatedInput"]["questions"], QUESTIONS)
        self.assertEqual(hso["updatedInput"]["answers"], {"Which parent branch?": "main"})
        self.assertFalse((self.answers / "toolu_ask1.json").exists())

    def test_answer_arriving_later_is_used(self):
        def later(fake):
            if fake.now - start > 3 and not (self.answers / "toolu_ask1.json").exists():
                self.answer({"action": "answer", "answers": {"Which parent branch?": "release/2.0-rc"}})
        fake = FakeTime(later)
        start = fake.now
        out = json.loads(self.run_hook(fake=fake))
        self.assertEqual(out["hookSpecificOutput"]["updatedInput"]["answers"], {"Which parent branch?": "release/2.0-rc"})

    def test_held_marker_exists_while_waiting_and_is_removed(self):
        seen = []

        def look(fake):
            seen.append((self.held / "toolu_ask1.json").exists())
            if len(seen) == 2:
                self.answer({"action": "vscode"})
        self.run_hook(fake=FakeTime(look))
        self.assertTrue(seen[0])
        self.assertFalse((self.held / "toolu_ask1.json").exists())

    def test_answer_in_vscode_prints_nothing(self):
        self.answer({"action": "vscode"})
        self.assertEqual(self.run_hook(), "")

    def test_empty_answers_print_nothing(self):
        self.answer({"action": "answer", "answers": {}})
        self.assertEqual(self.run_hook(), "")

    def test_gives_up_after_30_minutes(self):
        def keep_alive(fake):
            os.utime(self.data / "heartbeat", (fake.now, fake.now))
        fake = FakeTime(keep_alive)
        start = fake.now
        self.assertEqual(self.run_hook(fake=fake), "")
        self.assertGreaterEqual(fake.now - start, 1800)
        self.assertLess(fake.now - start, 1810)

    def test_lets_go_when_hark_stops(self):
        fake = FakeTime()
        start = fake.now
        self.assertEqual(self.run_hook(fake=fake), "")
        self.assertLess(fake.now - start, 200)

    def test_not_alive_prints_nothing_at_once(self):
        old = time.time() - 300
        os.utime(self.data / "heartbeat", (old, old))
        fake = FakeTime()
        start = fake.now
        self.assertEqual(self.run_hook(fake=fake), "")
        self.assertEqual(fake.now, start)
        self.assertFalse(self.held.exists() and any(self.held.iterdir()))

    def test_paused_prints_nothing_at_once(self):
        (self.data / "paused").touch()
        fake = FakeTime()
        start = fake.now
        self.assertEqual(self.run_hook(fake=fake), "")
        self.assertEqual(fake.now, start)

    def test_ignores_other_tools_agents_scripts_and_bad_ids(self):
        self.answer({"action": "answer", "answers": {"Which parent branch?": "main"}})
        self.assertEqual(self.run_hook({**PAYLOAD, "tool_name": "Bash"}), "")
        self.assertEqual(self.run_hook({**PAYLOAD, "agent_id": "agent-1"}), "")
        self.assertEqual(self.run_hook(env={"CLAUDE_CODE_ENTRYPOINT": "sdk-cli"}), "")
        self.assertEqual(self.run_hook({**PAYLOAD, "tool_use_id": "../evil"}), "")
        self.assertTrue((self.answers / "toolu_ask1.json").exists())

    def long_payload(self):
        question = ("Push 1 commit to gitlab.com/example/orders-service, branch fix/PROJ-2491 (MR !68)?\n"
                    "951686e test(PROJ-2491): check that the dispatched mark is a partial update. " + "Lint, types and all 94 tests pass. " * 8)
        label = "Yes, push and reply " + "x" * 250
        questions = [
            {"question": question, "header": "Push", "multiSelect": False,
             "options": [{"label": label, "description": "d"}, {"label": "No,  don't push", "description": "d"}]},
            {"question": "Which checks?", "header": "Checks", "multiSelect": True,
             "options": [{"label": "Unit", "description": ""}, {"label": "Lint", "description": ""}]},
        ]
        return {**PAYLOAD, "tool_input": {"questions": questions}}, question, label

    def test_long_question_answer_uses_the_original_text(self):
        payload, question, label = self.long_payload()
        # Hark keys answers with the shortened text the recorder gave it.
        self.answer({"action": "answer", "answers": {record_event._cut(question): record_event._cut(label), "Which checks?": "Unit, Lint"}})
        answers = json.loads(self.run_hook(payload))["hookSpecificOutput"]["updatedInput"]["answers"]
        self.assertEqual(answers, {question: label, "Which checks?": "Unit, Lint"})

    def test_shortened_labels_map_back_and_own_text_stays(self):
        payload, question, label = self.long_payload()
        self.answer({"action": "answer", "answers": {record_event._cut(question): "No, don't push", "Which checks?": "only the unit tests"}})
        answers = json.loads(self.run_hook(payload))["hookSpecificOutput"]["updatedInput"]["answers"]
        self.assertEqual(answers[question], "No,  don't push")
        self.assertEqual(answers["Which checks?"], "only the unit tests")

    def test_cut_matches_the_recorder(self):
        for text in ["short", "a  b\nc", "x" * 500, "  padded  "]:
            self.assertEqual(answer_hook._cut(text), record_event._cut(text))

    def test_bad_stdin_prints_nothing(self):
        out = io.StringIO()
        self.assertEqual(main(["--data-dir", str(self.data)], io.StringIO("not json"), {}, out), 0)
        self.assertEqual(out.getvalue(), "")


if __name__ == "__main__":
    unittest.main()
