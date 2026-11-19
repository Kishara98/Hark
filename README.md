# Hark

Minimize the window, not the work.

Hark is a small Windows app that tells you when Claude Code needs you, even when VS Code or the terminal is minimized: a question, a permission request, a finished turn, and how your agents are getting on.

Hark is an independent project. It is not made by, or affiliated with, Anthropic.

- A black pill at the top of the screen: "WebShop needs your answer", "2 sessions need you", "2 agents running".
- Popup cards at the bottom right for a question (with its options), a permission request, and a finished or stopped turn.
- Answer Claude's questions on the card: click an option, or open the full view (expand icon) to pick several, read the option descriptions or type your own answer. "Answer in VS Code" shows the normal dialog instead.
- Snooze 5 min, close, and Open VS Code / Open terminal (or Ctrl+Alt+H, held only while a card shows) to bring the right window forward. For VS Code it opens the exact Claude chat.
- Agent panel: click the pill to see each running agent with its pet, task, time and progress bar; click an agent for its to-do list and latest steps.
- Drag the pill (or the open panel) anywhere; it always stays on screen. Double click it, or pick "Reset pill position" in the tray menu, to put it back at the top centre.
- Tray icon with an amber dot while Claude waits, "Pause popups", "Reset pill position" and "Quit".
- Popups never take the keyboard focus from what you are typing.

## Requirements

- Windows 10 or 11 (.NET Framework 4.8 is built in).
- Claude Code running in WSL: the CLI in a WSL terminal, or the VS Code extension in a WSL window.
- Python 3 in WSL (most WSL distributions have it).

## Install

In WSL:

```bash
git clone https://github.com/Kishara98/Hark.git
cd Hark
./install.sh
```

This downloads the ready-built app from the latest release, installs it to `%LOCALAPPDATA%\Programs\Hark` (no admin rights), starts it and adds a Startup shortcut. It then adds Hark's hooks to `~/.claude/settings.json`; a backup of that file is made first and your own hooks stay as they are. Start a new Claude Code session, or reload VS Code, so it loads the hooks.

Remove everything with `./uninstall.sh`. Settings and logs in `%APPDATA%\Hark` and `%LOCALAPPDATA%\Hark` are kept.

### Real progress bars (optional)

Agents and tasks show real progress ("2 of 6 done") when Claude keeps a to-do list. Newer models only have the to-do tool when `~/.claude/settings.json` contains:

```json
"env": { "CLAUDE_CODE_ENABLE_TODO_TOOLS": "1", "CLAUDE_CODE_ENABLE_TASKS": "0" }
```

Without it the bar is an estimate learned from past runs (`~60% · step 7`): half full at the usual number of steps for that agent type, then still moving for longer runs.

## Privacy

Everything stays on your machine. The hooks write one small JSON file per event into `%LOCALAPPDATA%\Hark\inbox`, and the app deletes it after reading. Only short summaries are kept (a tool name, a command cut to 200 characters, a question and its options, an agent's description): no file contents and no prompts. Nothing is written while Hark is not running, and scripted runs (`claude -p`, Agent SDK) are ignored.

## How it works

Claude Code hooks (in WSL) run `hooks/record_event.py` in the background for each event. It writes the event into the inbox only while the app's `heartbeat` file is fresh. The app reads the inbox every second and draws the pill and the cards. The hooks are async, so Claude is never slowed.

One more hook, `hooks/answer_hook.py`, runs before each question (AskUserQuestion). While Hark runs and is not paused, it holds the question (VS Code shows Claude as working) and waits up to 30 minutes for an answer from the card. "Answer in VS Code", Open, Pause, Quit or the time limit let the question go to the normal dialog.

`hooks/agent_hook.py` asks Claude to keep a short to-do list for multi-step requests and agent tasks, so the bars show real progress. An agent's task name comes from the `agent-<id>.meta.json` file Claude Code keeps next to the agent's transcript, so it stays right after a Hark restart and when an agent is resumed.

## Settings and logs

- Settings: `%APPDATA%\Hark\settings.json` (`snoozeMinutes`, `openHotkey`, `paused`). Read at start.
- Log: `%LOCALAPPDATA%\Hark\logs\app.log` (start, the kind and project of each new card, Open results with the window title, errors). Never question text or answers.

## Known limits

- While Hark holds a question, the VS Code dialog only appears after "Answer in VS Code", Open, Pause, Quit or 30 minutes.
- Open finds windows by process (Code, WindowsTerminal) and the project name in the title. A CLI in a terminal whose title does not show the folder opens the first terminal.
- A session that ends without a SessionEnd event disappears after 2 hours without events.
- Claude Code running natively on Windows (not in WSL) is not supported.

## Build from source

Needs the .NET 10 SDK in `~/.dotnet` (WSL).

```bash
./install.sh --build                                   # build, install and start, then add the hooks
python3 -I -m unittest discover -s hooks -p 'test_*.py'
source scripts/env.sh && dotnet test tests/Hark.Core.Tests
scripts/demo.sh all                                    # sample question, permission, agents and done events
scripts/demo.sh clear
scripts/release.sh                                     # dist/Hark-win-x64.zip for a GitHub release
```

## License

MIT, see [LICENSE](LICENSE). Fonts: Geist and Geist Mono, SIL Open Font License ([OFL.txt](src/Hark.App/fonts/OFL.txt)).
