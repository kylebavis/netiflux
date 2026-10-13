# Netiflux

[![CI](https://github.com/kylebavis/netiflux/actions/workflows/ci.yml/badge.svg)](https://github.com/kylebavis/netiflux/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/kylebavis/netiflux?sort=semver)](https://github.com/kylebavis/netiflux/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A terminal client for [Miniflux](https://miniflux.app), built on .NET 10 and
[Terminal.Gui v2](https://github.com/gui-cs/Terminal.Gui). Cross-platform, developed and
tested primarily on Windows.

```
 Netiflux  ·  Unread  ·  16 unread  ·  12 starred
┌ Feeds ────────────┬ Unread (16) ───────────────┬ Designing for the terminal ──────┐
│ VIEWS             │ ●  Designing for the…   2h │ # Designing for the terminal     │
│ ▸ Unread       16 │ ●  A note on latency    4h │ *Engineering Blog · 5 min*       │
│   Today           │ ○  Something older      1d │ ─────────────────────────        │
│   Starred      12 │ ⤓  Saved earlier        2d │ Most terminal tools are built    │
│   All             │ ★  Starred piece        3d │ for one task at a time.          │
│ FEEDS             │                            │                                  │
│ ▸ Engineering   6 │                            │ ## Reading in place              │
│   Weekly Notes  1 │                            │                                  │
│   Broken Feed   ! │                            │ The reader follows your cursor,  │
│   show 335 more…  │                            │                                  │
└───────────────────┴────────────────────────────┴──────────────────────────────────┘
  j/k move · ⏎ open · m read · f star · s save · / search · ? help · q quit
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

Create the config file — `netiflux --where` prints its location
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
   `op read …`, `pass show miniflux`, `gpg -d ~/.miniflux.gpg`. This is the most secure option.

## Keys

Bindings follow Miniflux's own web shortcuts.

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
cursor.

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
standard `Base`, `Accent`, `Dialog`, `Menu`, and `Error`. Anything omitted falls
back to `Base`.

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
| `auto_fetch_truncated` | `false` | Auto-scrape entries that look like teasers |
| `track_saved_locally` | `true` | Remember pushed entries |

## Project layout

```
src/Netiflux.Core        API client, models, config, HTML→Markdown, local state
src/Netiflux             Terminal.Gui shell, list/reader panes, theming
tests/Netiflux.Tests    unit tests, plus end-to-end UI tests (Category=Ui)
```

```bash
dotnet test
```

## Releasing

Versioning is [semver](https://semver.org), driven by git tags. Pushing a tag
builds, tests and publishes.

```bash
git tag -a v0.2.0 -m "v0.2.0"
git push origin v0.2.0
```

That runs [`release.yml`](.github/workflows/release.yml), which:

1. validates the tag is a real semantic version;
2. runs the full test suite on Windows;
3. publishes self-contained single-file binaries for six platforms;
4. creates the GitHub release with archives, a combined `SHA256SUMS.txt`, and
   auto-generated notes.

A tag with a pre-release suffix (`v0.2.0-rc.1`) is marked as a pre-release on GitHub
rather than becoming "Latest". The version is injected into the build to facilitate
`netiflux --version`.

`workflow_dispatch` builds artifacts for a given version without publishing anything,
which is useful for checking a release before tagging.

Update [`CHANGELOG.md`](CHANGELOG.md) before tagging.
