# Changelog

All notable changes to CineLibrary are documented here.
Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [1.9.0] — 2026-04-26

### Added
- **📝 Notes** in the movie detail dialog. Type whatever you want about
  the film — your reaction, where you stopped, who you watched with —
  and hit Save. Empty state shows **+ Add note**, content state shows
  **Edit** / **Cancel** / **Save** controls. Always editable, even when
  the drive is offline.

### Storage (hybrid)
- Primary: new column `movies.note` (TEXT). DB save always succeeds.
- Sidecar: `cinelibrary-note.txt` written next to the movie's `.nfo`
  when the drive is online. MediaElch ignores it (won't be stripped on
  re-scrape), travels with the movie folder for portability across
  installs / machines.
- Scanner imports a sidecar to the DB only when the DB column is empty,
  so user edits made inside the app are never overwritten by a stale
  sidecar.

## [1.8.0] — 2026-04-26

### Added
- **▶ Continue Watching** sidebar shortcut. Hitting Play on any movie now
  stamps `last_played_at` on its row. The shortcut shows movies played at
  least once and not yet marked watched, sorted most-recent first, with a
  badge count. Hidden when there's nothing to continue. The OS player
  takes over once a movie launches, so resume position isn't tracked —
  the row stays in Continue Watching until you mark it Watched.

### Schema
- New column `movies.last_played_at` (epoch seconds, default 0).
  Migration runs automatically on first launch of v1.8.

## [1.7.1] — 2026-04-26

### Fixed
- **v1.7.0 installer crashed on launch** with `XamlParseException`. Root cause:
  WinUI 3 unpackaged + self-contained publish dropped the app-level
  `CineLibrary.pri` (compiled resource index for App.xaml/MainWindow.xaml/
  styles) into `bin/` but **not** `publish/`. App ran from the original
  `bin/` folder via path-proximity resource lookup, but the moment the
  publish output was copied anywhere else (e.g. into the Inno installer
  payload) `MainWindow.xaml` failed to parse because its `{StaticResource}`
  references couldn't be resolved. Added an MSBuild `AfterTargets="Publish"`
  step in `CineLibraryCS.csproj` that copies `$(AssemblyName).pri` from
  `$(OutDir)` to `$(PublishDir)`. v1.7.0 installer was unusable; install
  v1.7.1 to fix.
- Added `App.LogStartupCrash` — any unhandled exception during launch is now
  appended to `CineLibrary-Data/startup-crash.log` so future regressions can
  be diagnosed without a debugger.

## [1.7.0] — 2026-04-26 (broken — superseded by 1.7.1)

### Added
- Random-pick button — opens a random unwatched movie ("Surprise me")
- "Recently Added" sidebar shortcut
- Watchlist toggle button on movie card hover (alongside Watched)
- "Clear all filters" button when any filter is active
- First-launch empty-state CTA pointing to Drives → Add folder
- Burmese (မြန်မာ) user guide (`docs/USER_GUIDE_MM.md`)

### Changed
- Card-level Watched toggle clarifies action ("○ Mark Watched" / "✓ Watched")
- Scanner faster on big libraries: cached genre/director/actor name→id lookups
  (~5–10× fewer SQL round-trips per movie with many actors)
- Scanner skips re-copying cached artwork when source mtime ≤ dest mtime
- Search debounce uses `CancellationTokenSource` instead of leaking `Timer`s

### Fixed
- Stale `README.md` "next release" note removed

## [1.6.0] — 2026-04
### Added
- Auto-update notifier — checks GitHub on startup, shows quiet toast, never installs silently
- Sidebar section headers redesigned: orange-tinted card with thin dark-orange border

### Changed
- Movie grid auto-fits available width (`UniformGridLayout.ItemsStretch="Fill"`)
- Sidebar footer (Total Runtime / Avg Rating / Theme) now a rounded card
- Filter pills (All / Unwatched / Watched) squared-rounded
- Sidebar `Expander` controls replaced with custom `Button` + `ItemsRepeater`
  (the WinUI `Expander` template ignored size theme overrides — chrome was bulky)
- Tooltips removed from main toolbar buttons

## [1.5.0]
- Initial public release with multi-drive library, MediaElch nfo support,
  grid + list views (S/M/L/XL), full-text search, watchlist & favorites,
  CSV/HTML export, Mica backdrop, light/dark/system themes.
