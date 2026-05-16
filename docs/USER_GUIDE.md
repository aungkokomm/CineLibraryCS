# CineLibrary — User Guide

> A native Windows catalog for your movie collection.
> Browse, search, and track what you've watched across any number of drives.

This guide walks you from a fresh install to running a catalog of thousands
of movies in under five minutes.

---

> **What's new in v2.5.0** — Multi-select (Ctrl/Shift+click, Ctrl+A) with
> a bottom action bar for bulk add-to-list / watched / favorite / watchlist,
> drag-and-drop from cards onto sidebar lists, and per-list ✕ chips in the
> movie detail dialog. See *My Lists* and *Multi-select* below.

## Contents

1. [Before you start](#before-you-start)
2. [Install and first launch](#install-and-first-launch)
3. [Adding your movie folders](#adding-your-movie-folders)
4. [Browsing your library](#browsing-your-library)
5. [Search, filter, and sort](#search-filter-and-sort)
6. [Tracking what you've watched](#tracking-what-youve-watched)
7. [My Lists — group movies your way](#my-lists--group-movies-your-way)
8. [Multi-select — pick many, act once](#multi-select--pick-many-act-once)
9. [Statistics](#statistics)
10. [Multiple drives — online and offline](#multiple-drives--online-and-offline)
11. [Themes and sidebar](#themes-and-sidebar)
12. [Keyboard shortcuts](#keyboard-shortcuts)
13. [Exporting your catalog](#exporting-your-catalog)
14. [Updates](#updates)
15. [Where your data lives](#where-your-data-lives)
16. [Troubleshooting](#troubleshooting)

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

**Click** any poster to open the **movie detail** dialog. It opens
maximized and shows cast, ratings from multiple sources (IMDb / TMDb /
Rotten Tomatoes if your nfo has them), tech badges (resolution, codec,
audio), file size and duration, and quick toggles for watched /
favorite / watchlist. Press **Esc** to close, or use the close button
in the sticky bar that appears as you scroll.

**Double-click** a poster to play the movie immediately via your default
video player. If the movie's drive isn't connected, CineLibrary tells
you which drive to plug in.

### List view

Click the **☰** toggle next to the density picker for a compact list
showing title, year, rating, runtime, and watched status — useful for
scanning hundreds of titles at once.

### Sidebar shortcuts

The sidebar is grouped into four sections:

- **LIBRARY** — All Movies, Favorites, To Watch.
- **DISCOVER** — Continue Watching, Recently Added, Surprise me.
- **BROWSE** — banner-style pages: *By Genre*, *By Decade*, *By Rating*,
  *Collections*. Each entry shows count and a representative cover; click
  to filter your library to that slice.
- **TOOLS** — Statistics, Drives.
- **MY LISTS** — your custom lists. Click **+** to add one.
- **LIBRARIES** — one entry per drive root, with a live online/offline
  dot and a count.

Each collapsible section can be collapsed by clicking its header.

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

## My Lists — group movies your way

Built-in flags (Favorites / Watchlist) are just on/off. For everything
in between — *"Marvel chronological"*, *"Date-night picks"*, *"Top 10
of 2024"*, *"Movies I lent to Sai"* — make your own list under **MY
LISTS** in the sidebar.

### Creating a list

Two ways:

1. **From any movie** — right-click a poster (or open the detail
   dialog) → **📑 Add to list → + New list…** → type a name → Create.
   The movie is added to the new list automatically.
2. **Empty list first** — open the detail dialog of any movie, click
   **📑 Add to list → + New list…**, then immediately remove that
   movie if you only wanted the list itself.

A new entry appears under **MY LISTS** in the sidebar with a 📑 icon
and a count badge.

### Adding a movie to one or more lists

Right-click any poster (or open its detail dialog) → **📑 Add to list**.
The submenu shows every list you have, with a ✓ next to the ones the
movie is already in. Click a list to add; click a checked list to
remove. The movie can belong to as many lists as you like.

The detail dialog also shows a row of **📑 chips** under the action
buttons — one chip per list this movie is on. Tap the **✕** on any
chip to remove the movie from that list instantly.

### Renaming or deleting a list

Right-click the list in the sidebar:

- **Rename** — type a new name; the count and contents stay intact.
- **Delete list** — removes the list. Your movies are *not* deleted —
  they just leave that list.
- **📂 Copy movies to folder…** — bucket-export every online movie's
  source folder to a destination you pick. Useful for handing a
  curated set to a friend on a thumb drive.

### Viewing and editing a list

Click a list in the sidebar to switch the main view to just its
movies. From there you can:

- Right-click any poster → **Remove from this list** (via the same
  Add-to-list submenu — the list is already checked, click it to
  uncheck).
- Multi-select and bulk-remove (see next section).

---

## Multi-select — pick many, act once

Lots of common chores (mark 12 movies as watched, add a director's
filmography to a list, favorite a whole decade) are tedious one card
at a time. CineLibrary's multi-select fixes that.

### Selecting cards

- **Ctrl+click** a card → toggle it into the selection. Selected cards
  show a purple outline + a corner ✓.
- **Shift+click** → range-select from your last clicked card to this
  one.
- **Ctrl+A** → select every card currently visible.
- **Esc** → clear the selection.
- **Plain click** while a selection is active just clears it (so you
  don't accidentally open a detail dialog when trying to escape
  select-mode). With no active selection, plain click opens details
  as usual.

### The action bar

As soon as one or more cards are selected, a purple pill bar appears
at the bottom of the library:

> **X selected** · 📑 Add to list ▾ · ✓ Watched · 📌 Watchlist · ★ Favorite · 🗑 Remove from list · ✕

- **📑 Add to list ▾** opens a menu of your lists (plus *+ New list…*).
  Picking one adds *every* selected movie to that list.
- **✓ Watched / 📌 Watchlist / ★ Favorite** are smart toggles — if
  *any* selected movie doesn't yet have the flag, the action sets it
  on all of them; if every selected movie already has it, the action
  removes it from all of them.
- **🗑 Remove from list** only shows when you're currently viewing one
  of your lists. It removes the selected movies from *that* list (the
  movies themselves stay in your library and any other lists).
- Every bulk action shows a toast with an **Undo** button for ~6
  seconds. Click Undo to put everything back exactly as it was.

### Drag-and-drop

You don't even have to use the action bar — just **drag any selected
card onto a sidebar entry**:

- Drop on a **user list** → adds the whole selection to that list.
- Drop on **Favorites** → favorites everything in the selection.
- Drop on **To Watch** → adds everything to your watchlist.

The target row lights up purple while you hover over it. If you drag
a card that isn't part of the current selection, only that one card
gets added (and other selected cards stay selected — your selection
isn't disturbed).

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
| `Esc`            | Clear search, then clear multi-select        |
| `Ctrl+A`         | Select every card currently visible          |
| `Ctrl+click`     | Toggle a single card into the selection      |
| `Shift+click`    | Range-select from the last clicked card      |
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
