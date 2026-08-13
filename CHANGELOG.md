# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-08-13

First release.

### Added

- Terminal client for Miniflux built on .NET 10 and Terminal.Gui v2, cross-platform.
- Triage workflow with Miniflux-compatible keys: `j`/`k`, `o`, `m`, `f`, `s`, `v`,
  `g`-chords. Actions apply to the highlighted row without opening it, or to a
  `Space`-marked batch.
- Push to a third-party bookmark service with `s`, confirmed by the server before the
  saved marker appears. Saved entries are tracked locally because the Miniflux API does
  not report them.
- Reader pane rendering article HTML as Markdown, with images reduced to their alt text.
- Sidebar showing saved views plus feeds that have unread entries, with per-feed counts
  from `/v1/feeds/counters` and a toggle for the full list.
- Inline `/` search with live text, and `Esc` returning to the view you came from.
- Five bundled themes plus a live-preview picker on `Ctrl+T`; user themes can be added
  via `themes.json` without recompiling.
- Responsive layout that collapses to a single pane below 100 columns, and a zen reading
  mode on `z`.
- `--check`, `--where`, `--list-themes`, `--version` command-line helpers.
- Secret handling via environment variables or an `api_token_command`, so the API token
  need never be stored in plain text.

[Unreleased]: https://github.com/kylebavis/netiflux/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/kylebavis/netiflux/releases/tag/v0.1.0
