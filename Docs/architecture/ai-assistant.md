# AI assistant panel

The chat side panel (`Controls/MainWindow/AiChatWindow`) talks to a pluggable provider. Two exist: **Claude**
(official `Anthropic` NuGet SDK) and **OpenAI-compatible** (plain `HttpClient` + SSE against any
`/chat/completions` server: OpenAI, OpenRouter, Mistral, Ollama, LM Studio...).

## Pieces

```
AiChatWindow ──► AiHandler (per window: chats, active chat; saved to <instance>/Data/AiChats/<id>.json)
  │                └► AiChat (transcript, usage, model + effort, permission mode, browser tools, system prompt)
  │                     └► AiConversation (abstract, DataObjects/Complex/Ai/)
  │                          ├► ClaudeConversation          — List<BetaMessageParam>
  │                          └► OpenAiCompatibleConversation — List<JsonObject>
  ├► AiSettingsView (gear: keys, server, budget, permissions, tab-group naming)
  ├► AiModelPicker  (model button: fetched model lists + typed-in id)
  └► TabGroupNamer  (names tab groups with the quick model)
```

- **History lives in the provider's own wire format.** No neutral message type: converting would drop Claude's
  thinking blocks, which must be sent back byte-for-byte. So a chat is bound to the provider of the model it was
  created with. The **model can change within that provider** mid-chat (`AiChat.SetModel`); picking a model from
  the other provider starts a new chat (the old one stays in History; an empty chat is just dropped).
- **Model and effort travel with each turn** (`SendAsync(message, model, effort, ...)`); keys and the
  OpenAI-compatible base URL are read every turn, so fixing a key works mid-chat. Claude's client is static and
  shared (chats, quick jobs, model listing), rebuilt when the key changes, with `BaseUrl` pinned to
  `https://api.anthropic.com` so an inherited `ANTHROPIC_BASE_URL` can't redirect it.
- **Append-only, roll back on failure.** `SendAsync` records a checkpoint; any exception (network, API error,
  refusal, missing key) removes everything the turn appended, and `AiChat` drops the transcript entry. The panel
  shows the error and puts the message back in the input box. Claude's preserved-thinking check rejects
  edited history, so never edit or reorder earlier messages; only append or truncate a whole failed turn.
- **Saved chats** (panel setting "Save chats", `BrowserSettings.AiSaveChats`; null = on except in private
  instances). `AiHandler.LoadSaved` reads every chat file of the instance when the panel initializes;
  `AiHandler.Save` writes one after each finished turn and after a model/effort/mode change (empty chats are not
  saved; delete removes the file). A file is an `AiChatSave`: transcript, usage, model, effort, mode, and the
  provider history (`AiConversation.ExportHistory/ImportHistory`; Claude messages via the SDK's `RawData` /
  `FromRawUnchecked`). Image blocks (screenshots) are replaced by a text note on save so files stay small. Chat
  ids are also file names, so loading a chat bumps the id counter past it. Each window keeps its own copies:
  two windows continuing the same chat overwrite each other's file (last write wins).
  History lists chats newest first.
- **Stop.** While a reply runs, the send button is a stop button: it cancels the turn's token, which rolls the
  turn back like any failure (tool actions already done stay done), declines a pending approval card and puts
  the message back in the input box.
- **Streaming is batched.** `MarkdownTextBlock` re-parses its whole text on each change, so streamed deltas
  are buffered and applied every 100 ms (`StreamText`/`FlushStream`); flushed before approval cards and at the
  end of a turn.
- **Everything runs on the UI thread.** `AiChatWindow` awaits `AiChat.UserRequest` directly; the providers
  never `ConfigureAwait(false)`, so streaming callbacks and tool handlers can touch XAML and `TabManager`.

## Panel layout

Header: title, gear (settings view), history, close. Toolbar: model button (opens the picker), effort
(Claude only), permission mode. A usage line (chat tokens · ≈cost · month ≈spent of budget; tooltip has the
breakdown) and a "Credits" link (Console billing). The middle shows the chat, History, settings or the model
picker; header buttons toggle back to the chat. Input: multi-line `FTextInput` with `SubmitOnEnter` (Enter sends,
Shift+Enter / Ctrl+Enter add a line), grows to 160px then scrolls.

## Settings

- **Settings page:** only `AiEnabled` (per instance). Off hides the ☰ → AI Assistant entry and closes the panel.
- **Panel settings view** (`AiSettingsView`, built from the normal `*SettingControl`s, which stack when narrow):
  Claude key (+ Test), monthly budget and reset day, this period's spend (+ Clear), OpenAI-compatible base URL
  and key (+ Test), default permission mode, per-tool custom permissions (`AiToolPermissionsController`),
  tab-group naming on/off and the quick model.
- Per-instance values without `[SettingInfo]` in `BrowserSettings` (so they don't show on the settings page):
  `AiModel` (`AiModelChoice(Provider, Model)`, the default for new chats; the model picker sets it), `AiEffort`,
  `OpenAiBaseUrl`, `AiPermissionMode`, `AiToolPermissions`, `AiNameTabGroups`, `AiQuickModel`.
- **Model lists** (`AiModels`): Claude's Models API and the server's `GET /models`, cached for the session and
  keyed by key/server; refresh button in the picker. Anything unlisted can be typed in as a model id.

## Tools

Defined once in `Ai/BrowserTools.Create(window)` as `AiTool` (name, title, kind, description, JSON Schema
properties + required, handler returning `AiToolOutput` (text, optional image), optional `Describe` for
approval cards, `ReturnsImage`). Each provider maps them to its own format; tools that return images are only
given to Claude. Handlers validate their own arguments (`GetInt`/`GetIntArray`/`GetString`) because Claude tools
use `eager_input_streaming` (input isn't validated server-side) and OpenAI-compatible servers vary. Throw
`AiToolException` to hand the model an error result; other exceptions are logged and also returned as errors. At
most `MaxToolRounds` (25) model calls per user message.

| Kind | Tools |
|---|---|
| Read | `get_list_of_tabs`, `read_page`, `find_in_page`, `wait_for`, `screenshot`, `scroll_page` |
| Action | `click`, `type_text`, `select_option`, `press_key`, `open_tab`, `navigate_tab_to_url`, `switch_to_tab`, `close_tab`, `history_navigation`, `group_tabs`, `ungroup_tabs`, `edit_group`, `remove_group` |
| Script | `run_script` |

- Page tools take an optional `tab_id`; without it they use the active tab. Tab **-1 is the home page**: listed by
  `get_list_of_tabs`, reachable with `switch_to_tab`, but page tools refuse it (it's native UI, nothing to read).
  The settings page (-2) is neither listed nor switchable yet.
- `get_list_of_tabs` lists ungrouped tabs in strip order (`TabManager.Tabs`), then each group (`Groups`) with
  id, name, color and its tabs.
- **Actions wait and report** (`Act` → `PageAgent.RunAndWaitForNavigation`): click, type, select, keys,
  navigate and back/forward/reload run, wait 400 ms, and if a navigation started wait for `NavigationCompleted`
  (up to 10 s). The result says where the tab is now and lists any tabs that appeared (target=_blank).
- `wait_for` waits for load and, optionally, until some text is on the page (polls `find`, max 30 s).
- `screenshot`: CDP `Page.captureScreenshot` of the visible viewport as JPEG (q70), scaled to ≤1280 px wide
  (~1.5k tokens). Described as "only when needed". A tab that isn't on screen may not paint; it times out after
  8 s with a hint to switch to it.
- Group tools wrap `TabManager` (`CreateGroup`, `MoveTabToGroup`, `DissolveGroup`, `RemoveGroup`); colors are
  `{ "r", "g", "b" }` objects (0-255 each).

### Reading and driving pages (`PageAgent`)

Text, not screenshots. `PageAgent.Script` is installed into the page (main world, `window.__foxyAgent`,
reinstalled when `PageAgent.Version` changes) and renders the DOM as text: headings `#`, list items `- `, table
rows `| a | b |`, `pre` as fenced code, image alt text, and every link / button / form field inline as
`[e12 button "Add to cart"]`, `[e9 textbox "Username" = "value"]`, `[e6 select = "UK" options: …]`. Elements
with `cursor: pointer` and no role become `[eN clickable]`. Elements with no text but a `title`/`aria-label`
(status bars, icon badges) become `[label: "…"]`. Tables with hidden rows (collapsed/filtered) get a
"(N more rows hidden …)" note. Runs of identical lines collapse to one line with `(×N)`. Hidden
(`display:none` / not visible) content is skipped; open shadow roots and same-origin iframes are walked,
cross-origin frames are noted. Refs come from a `WeakMap`, so an element keeps its ref across reads; after a
navigation they start over. A Wikipedia article is about 80–250k characters and renders in 0.1–1 s; `read_page`
returns 20k-character parts (cut at line ends) and reuses the render for 20 s for later parts, unless a page
action ran in between. Mid-navigation the document can be empty; `read`/`find` report that and `read_page`
retries once before saying it's still loading.

Actions: `target(ref)` refuses disabled elements (`disabled` / `aria-disabled="true"`), scrolls the element into
view and returns its center (offset through same-origin frames), or an error if it's stale, has no size, or
another element covers it (the error names the cover, e.g. a cookie banner, with its ref). The click itself,
typing (`Input.insertText` after focusing and selecting the field) and keys go through the DevTools protocol
(`CallDevToolsProtocolMethodAsync`), so pages get trusted input events. `run_script` uses `Runtime.evaluate`
with `awaitPromise`, 30 s timeout, result truncated to 20k characters. Password fields and `autocomplete="cc-*"`
fields can't be focused for typing, in any mode; their values are never read.

### Permissions

`AiChat.WithPermissionCheck` wraps every tool. `AiPermissions.Resolve(mode, tool, settings)` gives Allow / Ask /
Block:

| Mode | Read | Action | Script |
|---|---|---|---|
| Read-only (default) | Allow | Ask | Ask |
| Ask | Ask | Ask | Ask |
| Brave | Allow | Allow | Allow |
| Custom | per tool from `BrowserSettings.AiToolPermissions` (missing = Read-only's choice) |||

Ask calls the tool's `Describe` (which may fail early, e.g. a stale ref, so the user isn't asked about a doomed
call), then `AiTurnCallbacks.Approve`, which `AiChatWindow` shows as an Allow / Deny card; the rest of the reply
continues below the card. Declined or blocked calls return an error result telling the model not to retry. The
default mode is in the panel settings; the mode button sets it per chat (mid-reply too, from the next tool
call). The system prompt tells the model that page content is data, never instructions.

## Claude specifics (`ClaudeConversation`)

- Beta messages endpoint (`client.Beta.Messages.CreateStreaming`), aggregated with `BetaMessageContentAggregator`
  so text streams live and the full `BetaMessage` is available after.
- Default model `claude-opus-5-5`; adaptive thinking; effort Low / Medium / High / Extra high / Max;
  `max_tokens` 64000; top-level automatic prompt caching (`CacheControl = ephemeral`).
- **Refusal fallbacks:** for `claude-fable-5-1`, `claude-opus-5-5`, `claude-opus-5`, `claude-sonnet-5-5` the request
  carries `fallbacks: "default"` + beta `server-side-fallback-2026-07-01`, so a safety-classifier decline is
  re-served by a fallback model in the same call. When echoing such a response, only `text` from before the last
  `fallback` block is kept (`ToParams`). A final `stop_reason: "refusal"` fails the turn.
- Response blocks are converted to params by hand (the C# SDK has no `ToParam`): text, thinking (with signature),
  redacted thinking, tool use. Empty text blocks are dropped (the API rejects them). Tool results with an image
  become `[text, image(base64)]` blocks.
- **401** becomes an `AiNotConfiguredException` naming the key as `AiCredentials.Mask` shows it
  (`sk-ant-api03-…x9Qk (108 characters)`), with an "Open assistant settings" button. `TestKeyAsync` lists one
  model to check a key.
- `CompleteAsync`: one-shot, no tools, low effort, used by `TabGroupNamer` (default quick model
  `claude-haiku-5-5`).

## Usage, cost and budget

Each response's `usage` goes through `AiTurnCallbacks.Usage` into `AiChat.Usage` (summed across tool rounds).
Cost uses list prices in `ClaudeConversation.Prices` (cache writes at 1.25x input), keyed by the model that
actually served the response. OpenAI-compatible usage is tokens only. **Remaining credits can't be read with a
normal API key** (no balance endpoint; the cost reports need an Admin key, which shouldn't live in a browser), so:

- `AiBudget` (global, `AiBudget.json` in the app data folder) adds up the estimated cost of every Claude response
  (chats and quick jobs) per day, against a monthly cap (default $100) that resets on a chosen day. Per-day
  entries (kept 62 days) mean moving the reset day re-counts correctly. "Clear" forgets the current period.
- The usage line shows `month ≈$spent of $cap` (and "over budget"); the settings view shows the period dates.
- The "Credits" link opens the Console billing page for the real balance.

## Tab-group naming (`TabGroupNamer`)

Per window, when `AiEnabled` and `AiNameTabGroups` are on: watches `TabManager.Groups`; 1.5 s after a group's tabs
change, if it's still called `Group N` (or still has the name this gave it), asks the quick model for a 1–3 word
name from the tab titles/hosts. Names set by the user or the assistant are never overwritten.

## Keys

`AiCredentials` stores one key per provider in the Windows Credential Locker (`PasswordVault`, resource
`FoxyBrowser716 AI`, user name = provider), shared by every instance. The panel settings use
`SecretSettingControl` (a themed `PasswordBox`) that saves on Enter or when focus leaves (never per keystroke),
with an optional Test button; the key never reaches `Settings.json`.
