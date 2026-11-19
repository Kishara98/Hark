import io
import json
import os
import tempfile
import time
import unittest
from pathlib import Path

from record_event import app_alive, build_record, main, redact, summarize_tool, write_event

ASK_PAYLOAD = {
    "session_id": "s-ask",
    "transcript_path": "/home/dev/.claude/projects/x/s-ask.jsonl",
    "cwd": "/home/dev/projects/WebShop",
    "permission_mode": "default",
    "hook_event_name": "PreToolUse",
    "tool_name": "AskUserQuestion",
    "tool_use_id": "toolu_ask",
    "tool_input": {
        "questions": [
            {
                "question": "Which parent branch should the new feature branch come from?",
                "header": "Parent",
                "multiSelect": False,
                "options": [
                    {"label": "release/2.0-rc", "description": "Release branch"},
                    {"label": "main", "description": "Main branch"},
                ],
            }
        ]
    },
}

AGENT_PAYLOAD = {
    "session_id": "s-agent",
    "cwd": "/home/dev/projects",
    "hook_event_name": "PreToolUse",
    "tool_name": "Grep",
    "tool_input": {"pattern": "pageSize"},
    "agent_id": "agent-abc",
    "agent_type": "Explore",
}

STOP_PAYLOAD = {
    "session_id": "s-stop",
    "cwd": "/home/dev/projects/Notes",
    "hook_event_name": "Stop",
    "stop_hook_active": False,
    "last_assistant_message": "Morning report sent. " + "x" * 1000,
}


class SummaryTests(unittest.TestCase):
    def test_summary_formats(self):
        self.assertEqual(summarize_tool("Bash", {"command": "mvn -q test", "description": "x"}), "Bash: mvn -q test")
        self.assertEqual(summarize_tool("Grep", {"pattern": "pageSize"}), 'Grep "pageSize"')
        self.assertEqual(summarize_tool("Glob", {"pattern": "**/*.cs"}), 'Glob "**/*.cs"')
        self.assertEqual(summarize_tool("Read", {"file_path": "/a/b/SplitConfig.java"}), "Read SplitConfig.java")
        self.assertEqual(summarize_tool("Edit", {"file_path": "/a/b/Main.cs", "old_string": "a", "new_string": "b"}), "Edit Main.cs")
        self.assertEqual(summarize_tool("Agent", {"description": "find split logic", "prompt": "long"}), "Agent: find split logic")
        self.assertEqual(summarize_tool("WebFetch", {"url": "https://x.y"}), "WebFetch")

    def test_summary_never_keeps_write_content(self):
        s = summarize_tool("Write", {"file_path": "/a/f.txt", "content": "z" * 10000})
        self.assertEqual(s, "Write f.txt")

    def test_truncates_to_200(self):
        self.assertEqual(len(summarize_tool("Bash", {"command": "x" * 500})), 200)

    def test_summary_handles_missing_input(self):
        self.assertEqual(summarize_tool("Bash", {}), "Bash")
        self.assertEqual(summarize_tool("Read", None), "Read")


REDACT_CASES = json.loads((Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "redact_cases.json").read_text(encoding="utf-8"))


class RedactTests(unittest.TestCase):
    """Likely secrets in commands become ***. The same table is used by the C# tests (HarkHook.exe)."""

    def test_table(self):
        for case in REDACT_CASES:
            with self.subTest(case=case["in"]):
                self.assertEqual(redact(case["in"]), case["out"])

    def test_command_summary_never_keeps_the_token(self):
        payload = {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Bash",
                   "tool_input": {"command": 'curl -H "Authorization: Bearer abc123def456ghi789" https://api.example.com'}}
        r = build_record(payload, {}, 1)
        self.assertEqual(r["summary"], 'Bash: curl -H "Authorization: Bearer ***" https://api.example.com')
        self.assertNotIn("abc123def456ghi789", json.dumps(r))

    def test_token_past_200_characters_is_hidden_before_the_cut(self):
        command = "echo " + "x" * 180 + " ghp_abcdefghijklmnopqrstuvwxyz123456"
        r = build_record({"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Bash", "tool_input": {"command": command}}, {}, 1)
        self.assertNotIn("ghp_abcdefghij", r["summary"])

    def test_messages_and_replies_are_cleaned_too(self):
        payload = {"session_id": "s", "cwd": "/p", "hook_event_name": "Stop", "last_assistant_message": "Set GITHUB_TOKEN=ghp_abcdefghijklmnopqrstuvwxyz123456 and run it."}
        self.assertEqual(build_record(payload, {}, 1)["last_message"], "Set GITHUB_TOKEN=*** and run it.")


class BuildRecordTests(unittest.TestCase):
    def test_build_record_question(self):
        r = build_record(ASK_PAYLOAD, {"CLAUDE_CODE_ENTRYPOINT": "claude-vscode"}, 1000)
        self.assertEqual(r["ts"], 1000)
        self.assertEqual(r["event"], "PreToolUse")
        self.assertEqual(r["session"], "s-ask")
        self.assertEqual(r["project"], "WebShop")
        self.assertEqual(r["source"], "vscode")
        self.assertEqual(r["tool"], "AskUserQuestion")
        q = r["questions"][0]
        self.assertEqual(q["question"], "Which parent branch should the new feature branch come from?")
        self.assertEqual(q["header"], "Parent")
        self.assertEqual(q["options"], ["release/2.0-rc", "main"])
        self.assertFalse(q["multi"])

    def test_build_record_keeps_option_descriptions(self):
        r = build_record(ASK_PAYLOAD, {}, 1)
        self.assertEqual(r["questions"][0]["descriptions"], ["Release branch", "Main branch"])

    def test_project_is_the_folder_claude_started_in(self):
        moved = {**ASK_PAYLOAD, "cwd": "/home/dev/projects/Hark"}
        r = build_record(moved, {"CLAUDE_PROJECT_DIR": "/home/dev/projects/"}, 1)
        self.assertEqual(r["project"], "projects")
        self.assertEqual(r["cwd"], "/home/dev/projects/Hark")

    def test_project_falls_back_to_cwd(self):
        self.assertEqual(build_record(ASK_PAYLOAD, {}, 1)["project"], "WebShop")

    def test_todo_list_is_kept(self):
        payload = {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "TodoWrite", "agent_id": "a1",
                   "tool_input": {"todos": [
                       {"content": "Fetch the release branch", "status": "completed", "activeForm": "Fetching"},
                       {"content": "Run the tests", "status": "in_progress", "activeForm": "Running the tests"},
                       {"content": "Write a summary", "status": "pending", "activeForm": "Writing"}]}}
        r = build_record(payload, {}, 1)
        self.assertEqual(r["todos"], [
            {"text": "Fetch the release branch", "status": "completed"},
            {"text": "Running the tests", "status": "in_progress"},
            {"text": "Write a summary", "status": "pending"}])

    def test_agent_launch_keeps_type_and_task(self):
        payload = {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_use_id": "t1",
                   "tool_input": {"description": "Find split logic", "subagent_type": "Explore", "prompt": "long secret prompt"}}
        r = build_record(payload, {}, 1)
        self.assertEqual(r["spawn_type"], "Explore")
        self.assertEqual(r["summary"], "Agent: Find split logic")
        self.assertNotIn("long secret prompt", json.dumps(r))

    def test_agent_launch_without_type_is_general_purpose(self):
        # Claude Code starts such an agent as general-purpose (seen live: "Developer agent: PROJ-2516 Task 3").
        payload = {"session_id": "s", "cwd": "/p", "hook_event_name": "PreToolUse", "tool_name": "Agent", "tool_use_id": "t1",
                   "tool_input": {"description": "Developer agent: Task 3", "model": "sonnet", "prompt": "p"}}
        self.assertEqual(build_record(payload, {}, 1)["spawn_type"], "general-purpose")

    def _agent_meta(self, root, description):
        folder = Path(root) / "proj" / "sess" / "subagents"
        folder.mkdir(parents=True)
        (folder / "agent-a11.meta.json").write_text(json.dumps({"agentType": "general-purpose", "description": description, "model": "sonnet"}))

    def _agent_payload(self, transcript):
        return dict(AGENT_PAYLOAD, agent_id="a11", transcript_path=str(transcript))

    def test_agent_event_carries_the_task_from_its_meta_file(self):
        # Claude Code keeps each agent's description next to its transcript; Hark gets it even after a restart or a resume.
        with tempfile.TemporaryDirectory() as root:
            self._agent_meta(root, "Developer agent: PROJ-2516 Task 3")
            r = build_record(self._agent_payload(Path(root) / "proj" / "sess.jsonl"), {}, 1)
            self.assertEqual(r["agent_task"], "Developer agent: PROJ-2516 Task 3")

    def test_agent_task_found_from_the_agent_transcript_path_too(self):
        with tempfile.TemporaryDirectory() as root:
            self._agent_meta(root, "Reviewer agent")
            r = build_record(self._agent_payload(Path(root) / "proj" / "sess" / "subagents" / "agent-a11.jsonl"), {}, 1)
            self.assertEqual(r["agent_task"], "Reviewer agent")

    def test_no_meta_file_means_no_agent_task(self):
        with tempfile.TemporaryDirectory() as root:
            self.assertNotIn("agent_task", build_record(self._agent_payload(Path(root) / "proj" / "sess.jsonl"), {}, 1))

    def test_main_thread_events_have_no_agent_task(self):
        self.assertNotIn("agent_task", build_record(ASK_PAYLOAD, {}, 1))

    def test_source_defaults_to_cli(self):
        self.assertEqual(build_record(ASK_PAYLOAD, {}, 1)["source"], "cli")
        self.assertEqual(build_record(ASK_PAYLOAD, {"CLAUDE_CODE_ENTRYPOINT": "cli"}, 1)["source"], "cli")

    def test_agent_fields_kept(self):
        r = build_record(AGENT_PAYLOAD, {}, 1)
        self.assertEqual(r["agent_id"], "agent-abc")
        self.assertEqual(r["agent_type"], "Explore")
        self.assertEqual(r["summary"], 'Grep "pageSize"')

    def test_last_message_cut_to_200(self):
        r = build_record(STOP_PAYLOAD, {}, 1)
        self.assertEqual(len(r["last_message"]), 200)
        self.assertTrue(r["last_message"].startswith("Morning report sent."))

    def test_last_message_is_first_real_line(self):
        reply = "## Summary\n\n**All 9 tasks are done.** Nothing is `pushed`.\n- item one\n- item two"
        r = build_record({**STOP_PAYLOAD, "last_assistant_message": reply}, {}, 1)
        self.assertEqual(r["last_message"], "All 9 tasks are done. Nothing is pushed.")

    def test_last_message_only_headings_keeps_the_heading(self):
        r = build_record({**STOP_PAYLOAD, "last_assistant_message": "\n## Done\n"}, {}, 1)
        self.assertEqual(r["last_message"], "Done")

    def test_empty_values_left_out(self):
        r = build_record(STOP_PAYLOAD, {}, 1)
        self.assertNotIn("tool", r)
        self.assertNotIn("agent_id", r)
        self.assertNotIn("questions", r)

    def test_never_keeps_prompt(self):
        r = build_record({"session_id": "s", "cwd": "/p", "hook_event_name": "UserPromptSubmit", "prompt": "secret plan"}, {}, 1)
        self.assertNotIn("secret plan", json.dumps(r))


class FileTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.data = Path(self.tmp.name)
        self.inbox = self.data / "inbox"

    def tearDown(self):
        self.tmp.cleanup()

    def test_write_event_atomic(self):
        path = write_event({"ts": 5, "event": "Stop"}, self.inbox)
        self.assertEqual([p.name for p in self.inbox.iterdir()], [path.name])
        self.assertTrue(path.name.endswith(".json"))
        self.assertEqual(json.loads(path.read_text(encoding="utf-8")), {"ts": 5, "event": "Stop"})

    def test_app_alive(self):
        hb = self.data / "heartbeat"
        self.assertFalse(app_alive(self.data, time.time()))
        hb.touch()
        self.assertTrue(app_alive(self.data, time.time()))
        old = time.time() - 300
        os.utime(hb, (old, old))
        self.assertFalse(app_alive(self.data, time.time()))

    def test_skips_when_app_not_alive(self):
        hb = self.data / "heartbeat"
        hb.touch()
        old = time.time() - 300
        os.utime(hb, (old, old))
        rc = main(["--data-dir", str(self.data)], io.StringIO(json.dumps(STOP_PAYLOAD)), {})
        self.assertEqual(rc, 0)
        self.assertFalse(self.inbox.exists() and any(self.inbox.iterdir()))
        hb.touch()
        rc = main(["--data-dir", str(self.data)], io.StringIO(json.dumps(STOP_PAYLOAD)), {})
        self.assertEqual(rc, 0)
        self.assertEqual(len(list(self.inbox.glob("*.json"))), 1)

    def test_skips_non_interactive_runs(self):
        (self.data / "heartbeat").touch()
        rc = main(["--data-dir", str(self.data)], io.StringIO(json.dumps(STOP_PAYLOAD)), {"CLAUDE_CODE_ENTRYPOINT": "sdk-cli"})
        self.assertEqual(rc, 0)
        self.assertFalse(self.inbox.exists() and any(self.inbox.iterdir()))

    def test_ts_taken_before_reading_stdin(self):
        (self.data / "heartbeat").touch()
        clock = {"now": 1000.0}

        class SlowStdin(io.StringIO):
            def read(self, *args):
                clock["now"] = 2000.0
                return super().read(*args)

        main(["--data-dir", str(self.data)], SlowStdin(json.dumps(STOP_PAYLOAD)), {}, clock=lambda: clock["now"])
        record = json.loads(next(self.inbox.glob("*.json")).read_text(encoding="utf-8"))
        self.assertEqual(record["ts"], 1_000_000)

    def test_main_never_fails(self):
        (self.data / "heartbeat").touch()
        self.assertEqual(main(["--data-dir", str(self.data)], io.StringIO("not json"), {}), 0)
        self.assertEqual(main([], io.StringIO(json.dumps(STOP_PAYLOAD)), {}), 0)
        self.assertFalse(self.inbox.exists() and any(self.inbox.iterdir()))


if __name__ == "__main__":
    unittest.main()
