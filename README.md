# Netiflux

[![CI](https://github.com/kylebavis/netiflux/actions/workflows/ci.yml/badge.svg)](https://github.com/kylebavis/netiflux/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kylebavis/netiflux?sort=semver)](https://github.com/kylebavis/netiflux/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A terminal client for [Miniflux](https://miniflux.app), built on .NET 10 and
[Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui). Cross-platform, developed and
tested primarily on Windows.

It is built around one workflow: **go through the unread list, read or skim what is
short, and push anything worth revisiting to your bookmark manager** via Miniflux's
"save to third-party service" integration.

```
 Netiflux  ·  Unread  ·  16 unread  ·  12 starred
┌ Feeds ────────────┬ Unread (16) ───────────────┬ How to read a paper ─────────────┐
│ VIEWS             │ ●  How to read a paper  2h │ # How to read a paper            │
│ ▸ Unread       16 │ ●  A note on latency    4h │ *Hacker News · S. Keshav · 5 min*│
│   Today           │ ○  Older thing          1d │ ─────────────────────────────    │
│   Starred      12 │ ⤓  Saved earlier        2d │ Researchers spend a great deal…  │
│   All             │ ★  Starred piece        3d │                                  │
│ FEEDS             │                            │ ## The three-pass approach       │
│ ▸ Reason.com    6 │                            │                                  │
│   Freddie deBoer 1│                            │ The first pass gives you a…      │
│   Anna's Blog   ! │                            │                                  │
│   show 335 more…  │                            │                                  │
└───────────────────┴────────────────────────────┴──────────────────────────────────┘
  j/k move · ⏎ open · m read · f star · s save · v browser · / search · ? help
```

## Install

Grab the archive for your platform from the
[latest release](https://github.com/kylebavis/netiflux/releases/latest) and unpack it.
The builds are self-contained, so **no .NET install is needed**. Binaries are published
for Windows, Linux and macOS on both x64 and arm64.

On macOS and Linux, mark it executable first:

```bash
chmod +x netiflux
```

Verify a download against the `SHA256SUMS.txt` published with the release.

To build from source instead you need the .NET 10 SDK:

```bash
dotnet build
```

## Getting started

You will need a Miniflux instance and an API token (Miniflux → Settings → API Keys).

Create the config file — `netiflux --where` prints its exact location
(`%APPDATA%\netiflux\config.json` on Windows, `~/.config/netiflux/config.json` elsewhere):

```json
{
  "server_url": "https://reader.example.com",
  "api_token_command": "op read op://Private/miniflux/token",
  "theme": "Netiflux Dark",
  "auto_mark_read": "on-scroll-end"
}
```

Then check the connection and run:

```bash
dotnet run --project src/Netiflux -- --check
```

```bash
dotnet run --project src/Netiflux
```

### Supplying the API token

Three options, in order of precedence:

1. **Environment** — `NETIFLUX_TOKEN` (or `MINIFLUX_API_KEY`). Also `NETIFLUX_URL`.
2. **`api_token`** in the config file — simplest, but the token sits in plain text. The
   file is written `chmod 600` on Unix; on Windows it inherits your user-profile ACL.
3. **`api_token_command`** — any shell command whose stdout is the token, e.g.
   `op read …`, `pass show miniflux`, `gpg -d ~/.miniflux.gpg`. The token never touches
   disk in plain text. This is the recommended option.

## Keys

Bindings follow Miniflux's own web shortcuts, so what you already know carries over.

### Moving

| Key | Action |
|---|---|
| `j` / `k` | Next / previous entry — scrolls the article when reading |
| `Space` | Mark the row and advance; pages down while reading |
| `G` / `g g` | Last / first entry |
| `Enter`, `o` | Open in the reader |
| `Esc` | Unwind one layer: clear selection → leave reader → leave search |
| `Tab` | Cycle sidebar → list → reader |

### Triage

| Key | Action |
|---|---|
| `s` | **Save to your bookmark service** |
| `m` | Toggle read / unread |
| `f` | Toggle star |
| `v` | Open the original in your browser |
| `F` | Ask Miniflux to scrape the full article text |
| `A` | Mark everything in this view read (asks first) |

Actions apply to the marked rows if there are any, otherwise to the row under the
cursor. Nothing needs to be opened first: `s` on a highlighted row saves it, and
`Space Space Space s` pushes three articles at once.

### Views and display

| Key | Action |
|---|---|
| `g u` / `g t` / `g s` / `g a` | Unread / Today / Starred / All |
| `Enter` (in sidebar) | Load that view, or expand / collapse the feed list |
| `/` | Search — opens a bar at the bottom; `Enter` runs it, `Esc` cancels |
| `r` / `R` | Reload this view / ask the server to poll every feed |
| `z` | Zen mode — reader fills the window |
| `\`, `Ctrl+B` | Show / hide the sidebar |
| `Ctrl+T` | Change theme |
| `?`, `F1` | Help |
| `q`, `Ctrl+Q` | Quit |

## Row markers

| Mark | Meaning |
|---|---|
| `●` | Unread |
| `○` | Read |
| `★` | Starred |
| `⤓` | Pushed to your bookmark service |
| `✦` | Saved and starred |
| `▸` | Marked for a batch action |

On legacy Windows consoles that cannot render these, Netiflux automatically falls back
to `* - + v # >`.

## Design notes

A few decisions worth knowing about, because they are not obvious from the outside.

**Saved state is tracked locally.** The Miniflux API has no "was this entry saved?"
field — `POST /v1/entries/{id}/save` is fire-and-forget and returns `202 Accepted`.
Without a local record there would be no way to show a saved marker, and during triage
you could not tell an article you already pushed from one you skipped. Netiflux keeps
its own log in `saved-entries.json` next to the config. Delete that file to reset it,
or set `"track_saved_locally": false` to turn the marker off.

**Most actions are optimistic, but saving is not.** Marking read and starring update the
screen immediately and reconcile with the server afterwards, rolling back and reporting
if the call fails — triage is a rhythm, and waiting on a round-trip breaks it. Saving
waits for the server to accept before showing the marker, because a save you believe
happened and did not is the one error here with lasting consequences.

**The sidebar shows what needs attention, not everything.** A large subscription list is
mostly quiet at any given moment — scrolling hundreds of silent feeds to reach the few
with new items is the opposite of triage. So the rail lists only feeds with unread
entries (ordered by count), plus any that are failing to parse, and hides the rest
behind a `show N more…` row that `Enter` expands. Per-feed counts come from
`/v1/feeds/counters`, since feed objects themselves carry none. The CATEGORIES section
appears only when there are at least two categories; a single one just duplicates
"Unread".

Broken feeds are always listed, marked with a trailing `!`, because a feed that has
stopped working looks exactly like one that simply has no news.

**Search is a bar, not a dialog, and it is undoable.** `/` opens a prompt at the bottom
of the window where the query is visible as it is typed — a modal dialog obscured the
one thing you needed to see. Running a search remembers the view you came from, and
`Esc` puts it back; search is a detour, not a destination you have to navigate out of by
hand. `Esc` generally unwinds one layer at a time: a marked selection first, then the
reader, then search.

**Images become their alt text.** Terminals cannot show pictures, and a Substack hero
image left as Markdown is several hundred characters of CDN URL dropped into the middle
of an article. Images render as `[image]`, or `[image: caption]` when the alt text says
something useful.

**The layout is responsive.** Below 100 columns the three panes stop being readable, so
the list and reader take turns instead: opening an entry switches to the reader and
`Esc` comes back. Above that width it is a normal split view.

**Highlighting an entry marks it read.** `auto_mark_read` defaults to `on-select`:
moving the cursor onto an entry is itself the act of reading it during triage, so
holding `j` clears the list as you go. Two details make that safe rather than annoying —
the mark waits for the cursor to *settle* (120 ms), so scrolling through to reach the
bottom does not clear everything on the way; and the initial cursor position after a
list loads is never marked, so opening the app does not consume the top article. Set
`on-open`, `on-scroll-end`, or `never` if you want the older behaviour.

## Theming

Five themes ship with the app: `Netiflux Dark` (default), `Netiflux Light`,
`Netiflux Gruvbox`, `Netiflux Nord`, and `Netiflux Rose Pine`. Terminal.Gui's own themes
are available too. Press `Ctrl+T` to switch — the list previews each theme live and
saves your choice on confirm.

```bash
dotnet run --project src/Netiflux -- --list-themes
```

To write your own, create `themes.json` in the config directory. A theme there replaces
a bundled one of the same name, so the easiest start is to copy a block out of
[`src/Netiflux/Resources/themes.json`](src/Netiflux/Resources/themes.json) and edit it.

```json
{
  "Themes": [
    {
      "My Theme": {
        "Schemes": [
          { "Base":        { "Normal": { "Foreground": "#cdd3de", "Background": "#13151c" } } },
          { "EntryUnread": { "Normal": { "Foreground": "#ffffff", "Background": "#13151c", "Style": "Bold" } } },
          { "EntrySaved":  { "Normal": { "Foreground": "#c4a7e7", "Background": "#13151c" } } }
        ]
      }
    }
  ]
}
```

Netiflux-specific scheme names are `Sidebar`, `EntryUnread`, `EntryRead`, `EntrySaved`,
`Reader`, `ReaderMeta`, `Banner`, `StatusGood`, and `StatusBad`, alongside Terminal.Gui's
standard `Base`, `Accent`, `Dialog`, `Menu`, and `Error`. Anything you leave out falls
back to `Base`, so a partial theme still works.

## Configuration reference

| Key | Default | Meaning |
|---|---|---|
| `server_url` | — | Miniflux base URL |
| `api_token` / `api_token_command` | — | See above |
| `theme` | `Netiflux Dark` | Active theme |
| `auto_mark_read` | `on-select` | `never`, `on-select`, `on-open`, or `on-scroll-end` |
| `page_size` | `100` | Entries fetched per page |
| `show_sidebar` | `true` | Sidebar visible at startup |
| `sidebar_width` | `28` | Sidebar columns |
| `list_width_percent` | `38` | Entry list share of the remaining width |
| `reader_max_width` | `88` | Cap on article text measure |
| `auto_fetch_truncated` | `false` | Auto-scrape entries that look like teasers |
| `refresh_interval_minutes` | `15` | Background unread refresh; `0` disables |
| `track_saved_locally` | `true` | Remember pushed entries |

## Project layout

```
src/Netiflux.Core        API client, models, config, HTML→Markdown, local state
src/Netiflux             Terminal.Gui shell, list/reader panes, theming
tests/Netiflux.Core.Tests  unit tests
tests/Netiflux.Ui.Tests    end-to-end: a real shell on a real main loop, driven by keys
```

```bash
dotnet test
```

`Netiflux.Ui.Tests` starts an actual `AppShell` on an actual Terminal.Gui main loop and
presses keys at it, because the bugs that matter in a TUI are focus and key-routing bugs
that method-level tests cannot see. Modal dialogs are the one gap — see
[the notes there](tests/Netiflux.Ui.Tests/README.md) for why, and what would fix it.

## Releasing

Versioning is [semver](https://semver.org), driven entirely by git tags. Pushing a tag
builds, tests and publishes; nothing else does.

```bash
git tag -a v0.2.0 -m "v0.2.0"
git push origin v0.2.0
```

That runs [`release.yml`](.github/workflows/release.yml), which:

1. validates the tag is a real semantic version and fails fast if not;
2. runs the full test suite on Windows — a red build blocks the release;
3. publishes self-contained single-file binaries for six platforms;
4. creates the GitHub release with archives, a combined `SHA256SUMS.txt`, and
   auto-generated notes.

A tag with a pre-release suffix (`v0.2.0-rc.1`) is marked as a pre-release on GitHub
rather than becoming "Latest". The version is injected into the build, so
`netiflux --version` on a downloaded binary reports exactly the tag it came from.

`workflow_dispatch` builds artifacts for a given version without publishing anything,
which is useful for checking a release before tagging.

Update [`CHANGELOG.md`](CHANGELOG.md) before tagging.

### Dependencies

[Dependabot](.github/dependabot.yml) opens weekly PRs for NuGet packages and workflow
actions. Terminal.Gui is grouped on its own deliberately: the app pins 2.4.17 and works
around two defects in it, so its updates want a real look rather than a rubber stamp.

## Known limitation

Terminal.Gui 2.4.17 marks its `ConfigurationManager` / `ThemeManager` statics obsolete in
favour of `TuiConfigurationBuilder`, but that replacement is not functional in this
release — it reports only the `Default` theme and its `SwitchTheme` fails, even for the
library's own built-in themes. Netiflux therefore uses the legacy API, isolated in
[`ThemeCatalog`](src/Netiflux/Theming/ThemeCatalog.cs), and should move over once the new
one works.
