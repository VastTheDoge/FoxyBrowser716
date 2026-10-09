# AI assistant

Provider rewrite (Mistral → Claude + OpenAI-compatible) and the page tools build but haven't been run in the app.
The page script's reading and element targeting were checked in Chromium (Wikipedia article + login form); the
DevTools-protocol click/type/key path has not run yet.

## Ask the user first
- [ ] **Chat panel UI issues.** The user saw several UI problems in the panel while testing (2026-10-09) but had no
      time to describe them. Ask what they are before changing the panel's layout or styling.
- [ ] **Message box size.** It's a fixed 26px single line (Enter sends). The user remarked that it isn't the small
      fixed-height box it used to be, so ask whether they want it to grow with the text (multi-line, Shift+Enter for a new line).

## Verify in the running app
- [ ] ☰ menu → AI Assistant opens the panel.
- [ ] Settings → AI Assistant shows Provider, the Claude/OpenAI fields, two password boxes, Permissions and the
      Custom permissions list; a pasted key survives a restart and does not appear in `Settings.json`.
- [ ] No key: sending shows "Add your Claude API key..." and an Open settings button; the message goes back into the input.
- [ ] Claude: a reply streams in; usage row updates (model · tokens · ≈$); tooltip shows cache reads from the 2nd turn on.
- [ ] Bad key: error bubble with the API's 401 message; fixing the key in settings and resending works in the same chat.
- [ ] "Credits" link opens the Console billing page (check the URL still lands on billing).
- [ ] Reading: "summarize this page", "what links are in the sidebar?", a long page (asks for more parts or uses find).
- [ ] Actions in Read-only mode: "search wikipedia for red fox" shows Allow/Deny cards for type_text; Deny stops it cleanly.
- [ ] Click lands on the right spot (check with page zoom ≠ 100% and on a page with a cookie banner: should report "covered by").
- [ ] type_text works on a React site (the field keeps the value after blur) and press_enter submits.
- [ ] Lightbulb menu switches mode mid-chat; Brave runs actions without cards; run_script still asks in Brave.
- [ ] Custom mode: Block on `click` makes the model say it can't click.
- [ ] Private window: page tools work.
- [ ] OpenAI-compatible against a local server (Ollama/LM Studio) with a tool-capable model.

## Follow-ups
- [ ] Persist chats across restarts (`AiHandler` TODO).
- [ ] Stop button / cancel a reply (providers already take a `CancellationToken`).
- [ ] Throttle `MarkdownTextBlock` re-renders while streaming (every delta re-parses the whole reply).
- [ ] More browser tools: search history, bookmarks, tab groups, downloads.
- [ ] Pages that scroll an inner container instead of the window: `scroll_page` up/down does nothing there (scrolling to a ref works).
