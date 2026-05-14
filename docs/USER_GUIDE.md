# CineLibrary — User Guide

> A native Windows catalog for your movie collection.
> Browse, search, and track what you've watched across any number of drives.

This guide walks you from a fresh install to running a catalog of thousands
of movies in under five minutes.

---

## Contents

1. [Before you start](#before-you-start)
2. [Install and first launch](#install-and-first-launch)
3. [Adding your movie folders](#adding-your-movie-folders)
4. [Browsing your library](#browsing-your-library)
5. [Search, filter, and sort](#search-filter-and-sort)
6. [Tracking what you've watched](#tracking-what-youve-watched)
7. [Statistics](#statistics)
8. [Multiple drives — online and offline](#multiple-drives--online-and-offline)
9. [Themes and sidebar](#themes-and-sidebar)
10. [Keyboard shortcuts](#keyboard-shortcuts)
11. [Exporting your catalog](#exporting-your-catalog)
12. [Updates](#updates)
13. [Where your data lives](#where-your-data-lives)
14. [Troubleshooting](#troubleshooting)

---

## Before you start

CineLibrary is a **reader**, not a scraper. It expects your movie folders
to be already scraped by [MediaElch](https://www.kvibes.de/mediaelch/) so
each movie sits in its own folder with a `.nfo` file and poster images
next to the video. A typical layout looks like this:

```
H:\Movies\
├── Inception (2010)\
│   ├── Inception.mkv
│   ├── Inception.nfo
│   ├── poster.jpg
│   └── fanart.jpg
├── The Matrix (1999)\
│   ├── The Matrix.mp4
│   ├── The Matrix.nfo
│   └── poster.jpg
└── ...
```

If you don't have `.nfo` files yet, run MediaElch over your library first.
Once your folders look like the example above, you're ready.

You'll also need:

- Windows 10 (build 19041) or Windows 11
- ~150 MB free space for the app itself, plus a small amount for the
  catalog database (a 10,000-movie library is around 50 MB)

---

## Install and first launch

1. Download the latest installer from the
   [releases page](https://github.com/aungkokomm/CineLibraryCS/releases/latest)
   — the file looks like `CineLibrary-vX.Y.Z-Portable-Setup.exe`.
2. Run it. The installer is **portable** — it will ask where to put
   CineLibrary (any folder you like — your `Documents`, an external
   drive, a USB stick). The app and its database live next to each other
   in that folder.
3. Launch `CineLibrary.exe`. The first window is empty until you tell it
   where your movies are — that's the next step.

> **Why portable?** Your library data lives in `CineLibrary-Data\` next
> to the executable. Move the folder, copy it to another machine, plug
> the USB stick into a friend's PC — your catalog goes with it.

---

## Adding your movie folders

Open the **Drives** page from the sidebar.

Click **Add folder** and point CineLibrary at the **root** of one of your
movie collections (e.g. `H:\Movies` or `E:\Films`). CineLibrary will
recursively scan that folder, read each `.nfo`, cache posters, and build
the catalog. Progress shows in the toast bottom-right.

A few tips:

- **Add as many roots as you like.** Each external drive, each
  subdivision of your collection — separate entries are fine.
- **Give drives friendly names.** Right-click a drive entry and rename
  it (e.g. *"Seagate Red 5TB — Movies"*) so the sidebar reads cleanly.
- **Re-scan after MediaElch updates.** If you re-scrape metadata or add
  new movies, hit **Rescan** on the drive entry; CineLibrary picks up
  the new and changed files.
- **Removing a drive is non-destructive** for your video files —
  CineLibrary only forgets its own catalog entries.

When the scan finishes, click **All Movies** in the sidebar.

---

## Browsing your library

The **All Movies** page is your home view. The sidebar shows shortcuts
to slice the catalog; the main area shows the movies.

### Grid view (default)

Posters laid out in a fluid grid that auto-fits the window. Use the
**S / M / L / XL** density picker in the top-right to pick poster size.
The grid stretches to fill the available width — bigger window means
more columns, automatically.

Click any poster to open the **movie detail** dialog: cast, crew,
ratings, runtime, plot, file path on disk, and quick toggles for
watched / favorite / watchlist.

### List view

Click the **☰** toggle next to the density picker for a compact list
showing title, year, rating, runtime, and watched status — useful for
scanning hundreds of titles at once.

### Sidebar shortcuts

The sidebar groups your library three ways:

- **LIBRARIES** — one entry per drive root, with a live online/offline
  dot and a count.
- **TOP GENRES** — your most-populated genres, top 8.
- **COLLECTIONS** — MediaElch collections (franchises, sagas, etc.)
  detected from your `.nfo` files.

Each section can be collapsed by clicking its orange header.

---

## Search, filter, and sort

### Search

The search box in the top bar matches across **title, actor, director,
and plot**. Type as you go — results update live. Press `Ctrl+F` from
anywhere to jump focus into the search box; press `Esc` to clear.

### Filter pills

Below the title, three pills toggle watched status:

- **All** — everything
- **Unwatched** — what you haven't seen yet
- **Watched** — your viewing history

Combine the pills with sidebar shortcuts (e.g. *"Unwatched in Top Genre:
Drama"*) to whittle down a long list quickly.

### Sort

The sort dropdown offers Title, Year, Rating, Runtime, and Date Added
— each in ascending or descending order. CineLibrary remembers your
last choice between sessions.

---

## Tracking what you've watched

Three personal flags, all toggle from the movie detail dialog:

- **Watched ✓** — mark a movie as seen. Affects the Watched / Unwatched
  filters and the Statistics page.
- **Favorite ⭐** — pin to the **Favorites** view in the sidebar.
- **Watchlist 📋** — queue for later. Shows in **To Watch** with a
  badge counter on the sidebar item.

These flags are local to your CineLibrary install — they don't write
back to your `.nfo` files, so MediaElch re-scrapes won't disturb them.

---

## Statistics

The **Statistics** page (sidebar) is a single-page view of your
collection at a glance:

- Total runtime (in days/hours), average rating, watch progress
- Top directors, top actors, top genres — by count
- Decade breakdown — see at a glance whether your library leans
  classic or modern
- Watchlist size

Numbers update as you toggle watched/favorite or add new drives. Use
this page to spot gaps ("hey, I have nothing from the 70s") or as
casual fun.

---

## Multiple drives — online and offline

CineLibrary is built for the case where your movies live on a stack of
external drives that aren't all plugged in at once.

- When a drive is **connected**, its entry in the sidebar shows a
  green dot, and its movies render in full color.
- When a drive is **disconnected**, the dot goes grey, and its movies
  show an `OFFLINE` overlay in the grid. They stay in the catalog and
  remain searchable — you just can't play them until you plug the
  drive back in.

Drive arrival and removal is **detected instantly** via Windows device
events — no polling, no lag. Plug a drive in mid-session and its
movies snap back to color within a second.

---

## Themes and sidebar

### Theme

Click the theme button in the bottom-left of the sidebar to cycle
between **System → Dark → Light**. CineLibrary uses Mica backdrop on
Windows 11 for a native feel and remembers your choice between
sessions.

### Collapsing the sidebar

`Ctrl+B` collapses or expands the sidebar (or click the `⮜` button at
the bottom-left). Collapsed mode gives the grid more room — useful on
small displays.

---

## Keyboard shortcuts

| Shortcut         | Action                                       |
|------------------|----------------------------------------------|
| `Ctrl+F`         | Focus the search box                         |
| `Esc`            | Clear search                                 |
| `Ctrl+B`         | Toggle sidebar                               |
| `Ctrl+Q`         | Quit                                         |
| `Ctrl+Shift+/`   | Show this shortcuts list in-app              |
| `PgDn` / `PgUp`  | Scroll the movie grid one viewport up/down   |
| `Home` / `End`   | Jump to the top / bottom of the movie list   |
| `↑` / `↓`        | Scroll by one row of cards                   |

Navigation shortcuts skip themselves when the search box has focus, so
typing in the search field still works normally.

---

## Exporting your catalog

Use the **Export** button in the top bar to dump the current view —
including any active filter and sort — to one of two formats:

- **CSV** — spreadsheet-friendly, one row per movie, columns for
  title, year, runtime, rating, genre, drive, watched status, and
  file path. Open in Excel or Numbers.
- **HTML** — a single self-contained HTML file with poster thumbnails
  inline. Useful for sharing your catalog with someone who doesn't
  have CineLibrary installed.

Exports respect your current filter — so to export only your watchlist,
filter to *Watchlist* first, then Export.

---

## Updates

CineLibrary checks GitHub on startup for new releases. When a new
version is out, you'll see a quiet toast bottom-right:

> *CineLibrary vX.Y.Z is available* &nbsp;&nbsp; **[ Download ]**

- Click **Download** to open the release page in your browser.
  CineLibrary never installs updates silently.
- Click **✕** to dismiss. CineLibrary remembers that you skipped this
  version and won't ask again until the next release.

If your machine is offline, the update check fails silently and the
app behaves normally.

---

## Where your data lives

Everything CineLibrary knows about your library lives in
`CineLibrary-Data\` next to the executable:

```
<install folder>\
├── CineLibrary.exe
├── CineLibrary-Data\
│   ├── cinelibrary.db        # SQLite catalog (movies, drives, prefs)
│   └── cache\                # Compressed poster/thumb cache
└── ...
```

That folder is **portable**. Copy or move the install folder anywhere
— another machine, a USB stick, an external drive — and the app and
catalog go together. There is **no telemetry**, no cloud sync, no
account required. Your library data never leaves your machine.

---

## Troubleshooting

### A movie I just added isn't showing up

Hit **Rescan** on the drive that contains it. CineLibrary catalogs
folders that have a `.nfo` file inside; if MediaElch hasn't scraped
that movie yet, it won't show up.

### Posters are missing or look wrong

CineLibrary reads `poster.jpg` (or `folder.jpg`, or the largest image
file alongside the `.nfo`) per MediaElch's conventions. Re-scrape with
MediaElch and rescan the drive in CineLibrary.

### Movies are showing OFFLINE even though the drive is plugged in

Open the **Drives** page and check the connection dot. If it's grey
despite the drive being plugged in, the drive's volume serial may have
changed (common after a re-format or partition change). Remove the
drive entry and re-add the same root — CineLibrary will reconcile.

### CineLibrary won't launch / crashes immediately

- Check that you're on Windows 10 build 19041 or later (`winver`).
- Make sure the install folder is on a local or external drive, not
  a network share — WinUI has rough edges with UNC paths.
- If the issue persists, open an issue on
  [GitHub](https://github.com/aungkokomm/CineLibraryCS/issues) with
  the exit code from the launcher script and a short description of
  what you were doing. Don't include personal paths in the title.

### I want to start over with a clean catalog

Close CineLibrary. Delete the `CineLibrary-Data\` folder next to the
executable. Re-launch — CineLibrary will create a fresh database. Your
movie files are untouched.

---

## Where to next

- **Found a bug or have an idea?**
  [Open an issue.](https://github.com/aungkokomm/CineLibraryCS/issues)
- **Want to see what's coming?** Check the
  [releases page](https://github.com/aungkokomm/CineLibraryCS/releases)
  for changelogs.
- **Want to contribute?** PRs are welcome. The codebase is C# / WinUI 3
  with `Microsoft.Data.Sqlite` and not much else — easy to dive into.

Happy cataloging. 🎬
