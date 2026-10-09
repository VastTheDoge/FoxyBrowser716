# AI assistant panel

The chat side panel (`Controls/MainWindow/AiChatWindow`) talks to a pluggable provider. Two exist: **Claude**
(official `Anthropic` NuGet SDK) and **OpenAI-compatible** (plain `HttpClient` + SSE against any
`/chat/completions` server: OpenAI, OpenRouter, Mistral, Ollama, LM Studio...).

## Pieces

```
AiChatWindow ──► AiHandler (per window: chats, active chat)
                   └► AiChat (transcript for display, usage total, browser tools, system prompt)
                        └► AiConversation (abstract, DataObjects/Complex/Ai/)
                             ├► ClaudeConversation          — List<BetaMessageParam>
                             └► OpenAiCompatibleConversation — List<JsonObject>
```

- **History lives in the provider's own wire format.** No neutral message type: converting would drop Claude's
  thinking blocks, which must be sent back byte-for-byte. So a chat is bound to the provider picked when it was
  created (`AiHandler.NewChat` reads `BrowserSettings.AiProvider`); switching provider affects new chats only.
- **Connection settings are read every turn** (key, model, effort, base URL), so fixing a key or switching model
  works mid-chat. `ClaudeConversation` rebuilds its `AnthropicClient` when the key changes.
- **Append-only, roll back on failure.** `SendAsync` records a checkpoint; any exception (network, API error,
  refusal, missing key) removes everything the turn appended, and `AiChat` drops the transcript entry. The panel
  shows the error and puts the message back in the input box. Claude's preserved-thinking check rejects
  edited history, so never edit or reorder earlier messages; only append or truncate a whole failed turn.
- **Everything runs on the UI thread.** `AiChatWindow` awaits `AiChat.UserRequest` directly; the providers
  never `ConfigureAwait(false)`, so streaming callbacks and tool handlers can touch XAML and `TabManager`.

## Tools

Defined once in `Ai/BrowserTools.Create(window)` as `AiTool` (name, title, kind, description, JSON Schema
properties + required, handler, optional `Describe` for approval cards). Each provider maps them to its own format.
Handlers validate their own arguments (`GetInt`/`GetString`) because Claude tools use `eager_input_streaming`
(input isn't validated server-side) and OpenAI-compatible servers vary. Throw `AiToolException` to hand the model
an error result; other exceptions are logged and also returned as errors. At most `MaxToolRounds` (25) model
calls per user message.

| Kind | Tools |
|---|---|
| Read | `get_list_of_tabs`, `read_page`, `find_in_page`, `scroll_page` |
| Action | `click`, `type_text`, `select_option`, `press_key`, `open_tab`, `navigate_tab_to_url`, `switch_to_tab`, `close_tab`, `history_navigation` |
| Script | `run_script` |

Page tools take an optional `tab_id`; without it they use the active tab (an error if that's the home or settings page).

### Reading and driving pages (`PageAgent`)

Text, not screenshots. `PageAgent.Script` is installed into the page (main world, `window.__foxyAgent`,
reinstalled when `PageAgent.Version` changes) and renders the DOM as text: headings `#`, list items `- `, table
rows `| a | b |`, `pre` as fenced code, image alt text, and every link / button / form field inline as
`[e12 button "Add to cart"]`, `[e9 textbox "Username" = "value"]`, `[e6 select = "UK" options: …]`. Elements
with `cursor: pointer` and no role become `[eN clickable]`. Hidden (`display:none` / not visible) content is
skipped; open shadow roots and same-origin iframes are walked, cross-origin frames are noted. Refs come from a
`WeakMap`, so an element keeps its ref across reads; after a navigation they start over. A Wikipedia article is
about 250k characters and renders in about 1 s; `read_page` returns 20k-character parts (cut at line ends) and
reuses the render for 20 s for later parts, unless a page action ran in between.

Actions: `target(ref)` scrolls the element into view and returns its center (offset through same-origin frames),
or an error if it's stale, has no size, or another element covers it (the error names the cover, e.g. a cookie
banner, with its ref). The click itself, typing (`Input.insertText` after focusing and selecting the field) and
keys go through the DevTools protocol (`CallDevToolsProtocolMethodAsync`), so pages get trusted input events.
`run_script` uses `Runtime.evaluate` with `awaitPromise`, 30 s timeout, result truncated to 20k characters.
Password fields and `autocomplete="cc-*"` fields can't be focused for typing, in any mode; their values are never
read.

### Permissions

`AiChat.WithPermissionCheck` wraps every tool. `AiPermissions.Resolve(mode, tool, settings)` gives Allow / Ask /
Block:

| Mode | Read | Action | Script |
|---|---|---|---|
| Read-only (default) | Allow | Ask | Ask |
| Ask | Ask | Ask | Ask |
| Brave | Allow | Allow | Ask |
| Custom | per tool from `BrowserSettings.AiToolPermissions` (missing = Read-only's choice) |||

Ask calls the tool's `Describe` (which may fail early, e.g. a stale ref, so the user isn't asked about a doomed
call), then `AiTurnCallbacks.Approve`, which `AiChatWindow` shows as an Allow / Deny card; the rest of the reply
continues below the card. Declined or blocked calls return an error result telling the model not to retry. The
default mode is a setting; the lightbulb button sets it per chat (mid-reply too, from the next tool call). Custom
choices are edited by `AiToolPermissionsController` (Settings → AI Assistant). The system prompt tells the model
that page content is data, never instructions.

## Claude specifics (`ClaudeConversation`)

- Beta messages endpoint (`client.Beta.Messages.CreateStreaming`), aggregated with `BetaMessageContentAggregator`
  so text streams live and the full `BetaMessage` is available after.
- Default model `claude-opus-5-5`; adaptive thinking; effort from settings (`Low`/`Medium`/`High`/`Max`);
  `max_tokens` 64000; top-level automatic prompt caching (`CacheControl = ephemeral`).
- **Refusal fallbacks:** for `claude-fable-5-1`, `claude-opus-5-5`, `claude-opus-5`, `claude-sonnet-5-5` the request
  carries `fallbacks: "default"` + beta `server-side-fallback-2026-07-01`, so a safety-classifier decline is
  re-served by a fallback model in the same call. When echoing such a response, only `text` from before the last
  `fallback` block is kept (`ToParams`). A final `stop_reason: "refusal"` fails the turn.
- Response blocks are converted to params by hand (the C# SDK has no `ToParam`): text, thinking (with signature),
  redacted thinking, tool use. Empty text blocks are dropped (the API rejects them).

## Usage and cost

Each response's `usage` goes through `AiTurnCallbacks.Usage` into `AiChat.Usage` (summed across tool rounds).
The panel's second header row shows model · tokens in/out · estimated cost; the tooltip breaks out cached input
and the window total. Cost uses list prices in `ClaudeConversation.Prices` (cache writes at 1.25x input), keyed
by the model that actually served the response. OpenAI-compatible usage is tokens only. **Remaining credits
can't be read with a normal API key** (it needs an org Admin key, which shouldn't live in a browser), so the
"Credits" link opens the Console billing page.

## Keys

`AiCredentials` stores one key per provider in the Windows Credential Locker (`PasswordVault`, resource
`FoxyBrowser716 AI`, user name = provider), shared by every instance. The settings expose them as `[JsonIgnore]`
properties with `SettingInfo.Secret = true`, which renders a `SecretSettingControl` (a themed `PasswordBox`); the
property getter/setter reads and writes the vault directly, so the key never reaches `Settings.json`.
