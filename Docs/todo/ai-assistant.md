# AI assistant

Claude chat works in the app (2026-10). Browser testing by Claude found the bugs fixed on 2026-10-12 (home tab,
load race in read_page, new-tab reporting, title/aria-label text, hidden table rows, disabled clicks) and the
settings moved into the panel. The 2026-10-12 changes build but haven't been run in the app yet.

## Waiting on the user
- [ ] **Settings page for the assistant.** The user has an idea for how the assistant could open/use the settings
      page ("yes, with a huge *"). Ask before building it; until then `switch_to_tab(-2)` is refused.
- [ ] **Chat panel UI issues.** The user saw UI problems on 2026-10-09 but doesn't remember them; they'll report
      them when they see them again. Layout direction so far: model/effort/mode at the top, multi-line input.

## Verify in the running app
- [ ] ☰ menu → AI Assistant opens the panel; Settings page → AI assistant Off hides the menu entry and closes the panel.
- [ ] Gear opens the assistant settings in the panel; gear again goes back to the chat.
- [ ] Claude key: paste + Enter (or click away) saves; Test says "Key works."; a bad key shows the masked key; the key survives a restart and isn't in `Settings.json`.
- [ ] Model button lists Claude models (and the OpenAI-compatible server's when set up); search narrows; a typed-in id works.
- [ ] Switching Claude models mid-chat keeps the history; picking an OpenAI-compatible model starts a new chat.
- [ ] Effort button hidden for OpenAI-compatible models; Extra high works on Opus 5.5.
- [ ] Input: Enter sends, Shift+Enter and Ctrl+Enter add a line, the box grows then scrolls.
- [ ] Usage line: chat tokens + ≈cost + "month ≈$x of $100"; the settings view shows the same period; Clear resets it; changing the reset day re-counts.
- [ ] `AiBudget.json` is written to the app data folder and shared by two instances.
- [ ] Brave mode runs `run_script` without a card.
- [ ] `get_list_of_tabs` shows Tab -1 (home) and groups; "go back to home" switches to it.
- [ ] Click on a target=_blank link reports the new tab; navigate + read right away doesn't crash.
- [ ] `wait_for` with text on a page that loads data late (e.g. a status page).
- [ ] `screenshot` on the active tab (and what happens on a background tab).
- [ ] Group tools: create with name/color, add to existing, ungroup, rename, remove (keep / close tabs).
- [ ] Tab-group naming: turn it on, make a group with a few tabs; it gets a short name; renaming it by hand sticks.
- [ ] Reading: "summarize this page", a long page (parts / find), a status page with title-only bars.
- [ ] Actions in Read-only mode: Allow/Deny cards; Deny stops cleanly.
- [ ] Click lands on the right spot (page zoom ≠ 100%; cookie banner → "covered by").
- [ ] type_text on a React site keeps the value after blur; press_enter submits.
- [ ] Custom mode: Block on `click` makes the model say it can't click.
- [ ] Private window: page tools work.
- [ ] OpenAI-compatible against a local server (Ollama/LM Studio) with a tool-capable model.

## Follow-ups
- [x] Persist chats across restarts (`AiHandler` TODO).
- [x] Stop button / cancel a reply (providers already take a `CancellationToken`).
- [x] Throttle `MarkdownTextBlock` re-renders while streaming (every delta re-parses the whole reply).
- [ ] More browser tools: search history, bookmarks, downloads.
- [x] Pages that scroll an inner container instead of the window: `scroll_page` up/down does nothing there (scrolling to a ref works).
- [ ] Effort levels per model: older models (4.6) reject Extra high; could read `capabilities` from the Models API.
