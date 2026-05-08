# Changelog

All notable changes to CineLibrary are documented here.
Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [2.0.0] — 2026-05-08

A big visual + feature refresh. The headline things you'll notice:

### New
- **My Lists** — make your own lists ("Date night", "80s sci-fi", etc.)
  in the sidebar. Right-click a movie or click *Add to list* in the
  detail dialog to drop it in.
- **Copy a list to a folder** — right-click a list → *Copy movies to
  folder…* and CineLibrary copies every movie's full folder (video +
  posters + nfo + extras) to a destination drive of your choice. Asks
  before overwriting, skips offline movies, shows live progress.
- **Continue Watching** sidebar shortcut — anything you've hit Play on
  but haven't marked Watched, sorted most-recently-played first.
- **Recently Added** sidebar shortcut — sorted by date added.
- **Surprise me** — random unwatched movie, prefers movies on connected drives.
- **Notes** on the movie detail dialog — write your own thoughts, where
  you stopped, who you watched with. Saved alongside the movie's `.nfo`
  in a tiny sidecar file so they travel with your library.
- **Refresh changes** button on the Drives page — quickly picks up
  anything you've re-scraped in MediaElch without doing a full rescan.
- **Clickable chips in the detail dialog** — click a genre, director,
  actor, or studio to filter the library by that.

### Looks
- New sidebar layout with **LIBRARY / DISCOVER / TOOLS** sections.
- Modern Fluent icons replace the emoji ones.
- Selected nav item now shows a purple accent bar so you know where you are.
- Footer reorganised into clean stat tiles (Runtime / Avg Rating).
- Light theme polish.

### Fixes
- Mark Watched now updates immediately on the card, regardless of how
  many times the card has scrolled in and out of view.
- Movie count in the top bar is now honest — *"60 of 1,200 movies"*
  instead of pretending the page total is the library total.
- Actor / collection counts no longer split across whitespace-drift
  duplicates ("Tom Hanks " vs "Tom Hanks" are now one row). One-shot
  cleanup on first launch heals existing libraries.
- "Refresh changes" no longer pulls in TV episode `.nfo` files as fake
  movies, and cleans up any strays from earlier preview builds.
- Scanner skips Windows system folders (`System Volume Information`
  etc.), no more *"access denied"* errors on drive-root scans.
- Fresh installs no longer crash on launch (missing `CineLibrary.pri`
  was the culprit, fixed in the build pipeline).

## [1.9.2] — 2026-04-26

### Added
- **Copy list to folder** — right-click a list in the sidebar and pick
  *📂 Copy movies to folder…* to copy every online movie's source folder
  (video + .nfo + posters + everything inside) to a destination drive or
  directory. Source files are never touched. Free-space check up front;
  if any target folders already exist, you get one prompt — Skip / Overwrite.
  Offline drives are silently skipped and reported in the summary.
- **Right-click a movie card** for quick Watched / Favorite / Watchlist
  toggles plus an *Add to list ▶* submenu. Same flyout works on list-view rows.
- **My Lists** — user-defined custom lists in the sidebar. Click **+** in
  the section header to create one ("Date night", "80s sci-fi", anything).
  In the movie detail dialog, **📑 Add to list** flyout shows checkboxes
  for every list and a **+ New list…** entry. Right-click a list in the
  sidebar to rename or delete.

### Fixed
- **"X movies" header** in the library top bar was misleading — it counted
  rows loaded so far (per-page), not rows that matched the filter. Now
  reads e.g. *"60 of 1,200 movies"* while paging in, *"850 movies"* once
  everything fits.
- **Mark Watched on the card didn't reflect immediately** in some views.
  `MovieListItem` is now an `ObservableObject`; cards and rows subscribe
  to its `PropertyChanged` so `IsWatched`, `IsFavorite`, `IsWatchlist`
  changes update the visible card without a re-render.
- **Actor / collection counts split across whitespace-drift duplicates.**
  Names like "Tom Hanks" and "Tom Hanks " were distinct rows, undercounting
  the actor's filtered movies and breaking collections like *James Bond*
  when MediaElch wrote the set name with inconsistent spacing. Scanner
  now trims + collapses whitespace on insert. A one-shot migration on
  first launch of v1.9.2 normalizes existing rows and merges duplicates
  (actors, directors, genres, sets) so the fix retroactively heals the
  catalog without a rescan.

### Schema
- New tables `user_lists`, `user_list_movies` (cascade on delete).
  Migration runs automatically on first launch; existing DBs gain them
  via `IF NOT EXISTS`.

## [1.9.1] — 2026-04-26

### Fixed
- **Genre / Director / Studio / Cast chips in the detail dialog** looked
  clickable but did nothing. Now each navigates to the library page with
  the corresponding filter applied (e.g. click "Drama" → All Movies ›
  DRAMA, click "Hrishikesh Mukherjee" → Directed by Hrishikesh Mukherjee,
  click an actor card → Movies with <name>). Studio is a new filter type
  added in 1.9.1.

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
