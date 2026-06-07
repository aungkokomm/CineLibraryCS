# CineLibrary — User Guide

> A native Windows catalog for your movie collection.
> Browse, search, and track what you've watched across any number of drives.

This guide walks you from a fresh install to running a catalog of thousands
of movies in under five minutes.

---

> **What's new in v2.8.0** — **TV Shows!** Folders with a `tvshow.nfo`
> are now detected as shows, with a single show page (poster, plot,
> cast, season-by-season episode rows, per-episode watched tracking,
> and a "▶ Play next unwatched" button). See *TV Shows* below.

## Contents

1. [Before you start](#before-you-start)
2. [Install and first launch](#install-and-first-launch)
3. [Adding your movie folders](#adding-your-movie-folders)
4. [Browsing your library](#browsing-your-library)
5. [Search, filter, and sort](#search-filter-and-sort)
6. [Tracking what you've watched](#tracking-what-youve-watched)
7. [TV Shows](#tv-shows)
8. [My Lists — group movies your way](#my-lists--group-movies-your-way)
9. [Tags — your own labels](#tags--your-own-labels)
10. [Discovery: Recently Watched, On This Day, Surprise Me](#discovery)
11. [Multi-select — pick many, act once](#multi-select--pick-many-act-once)
12. [State that travels with your drives](#state-that-travels-with-your-drives)
13. [Backup and restore](#backup-and-restore)
14. [Statistics](#statistics)
15. [Multiple drives — online and offline](#multiple-drives--online-and-offline)
16. [Themes and sidebar](#themes-and-sidebar)
17. [Settings](#settings)
18. [Keyboard shortcuts](#keyboard-shortcuts)
19. [Exporting your catalog](#exporting-your-catalog)
20. [Updates](#updates)
21. [Where your data lives](#where-your-data-lives)
22. [Troubleshooting](#troubleshooting)

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

The search box lives in the **title bar** at the top of the window, so
it's available from **every page** — start typing anywhere and
CineLibrary jumps straight to the matching movies (and shows). It matches
across **title, original title, year, actor, and director** (and
collection names). It deliberately doesn't search plot text — a short
word like "hit" appears in hundreds of plot summaries, which used to bury
the titles you actually wanted. Title matches are ranked first, so the
closest result is at the top. Type as you go — results update live. Press
`Ctrl+F` from anywhere to jump into it; press `Esc` to clear it and step
back out.

The dropdown to the left of the search box sets the **scope**:
**All** (the default), **Title** to match titles only, or **Cast &
crew** to find everything an actor or director worked on.

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

## TV Shows

As of **v2.8.0**, CineLibrary catalogs TV shows alongside your movies.

### What gets detected as a show

Any folder that contains a **`tvshow.nfo`** is treated as a TV show.
Episodes are the video files inside it whose names carry an
`SxxExx` marker, each with a matching `.nfo` — exactly what MediaElch
(or CineLibrary Essentials) produces:

```
H:\TV\Dark\
├── tvshow.nfo                       ← show metadata
├── poster.jpg  fanart.jpg
├── .actors\                         ← cast thumbnails
├── Dark - S01E01 - Secrets.mkv
├── Dark - S01E01 - Secrets.nfo
├── Dark - S01E01 - Secrets-thumb.jpg
├── Dark - S01E02 - Lies.mkv
└── …
```

Seasons are read straight from the `Sxx` in each filename, so a show
folder can hold every season flat in one place. Movies are unaffected —
a single drive can hold both movies and shows.

### Finding your shows

Click **All TV Shows** in the sidebar (under All Movies). You get a
grid of show cards, each with its poster, year, and a watched-progress
bar. The sidebar badge shows how many shows you have.

### The show page

Click a show to open its page — everything on one scroll, no
drilling:

- **Header** — poster, plot, year · rating · status, a watched
  roll-up ("12/62 watched"), genre chips, a **cast strip**, and
  **☆ Favorite** / **📋 Watchlist** buttons.
- **▶ Play next** — jumps straight to the first unwatched episode
  (by season, then episode). Reads "✓ All watched" once you're done.
- **Season rows** — each season is a horizontal row of episode
  cards (scroll sideways, like Netflix). Each card shows the
  episode thumbnail, `SxxExx`, title, runtime/rating, an
  **○ / ✓ Mark-watched** toggle, and **▶ Play** on hover.

### Watching episodes

- **Double-click** an episode card (or its **▶ Play**) to open it in
  your default video player. Playing also marks it watched.
- **Single-click** an episode card to open its **details** — full plot,
  air date, rating, runtime, plus tech info pulled from the `.nfo`
  (resolution, HDR, video/audio codec, audio languages, subtitles,
  container, file size). The dialog has Play and Mark-watched buttons.
- The **○ / ✓** toggle on each card flips watched state without
  playing. The show's progress roll-up updates instantly.
- Each **season header** has **▶ Play season** (first unwatched in that
  season) and **Mark all watched / unwatched**.

### Adding shows to lists

My Lists are independent buckets — a list can hold **movies and shows
together**. Use **📑 Add to list** in the show header. When you open
that list from the sidebar, its shows appear in a row at the top and
its movies in the grid below. (See *My Lists*.)

### TV in Statistics

The Statistics page gains a **📺 TV Shows** section once you have shows:
total shows, total episodes, combined episode runtime, average show
rating, and an episodes-watched progress bar.

### Favorite / Watchlist for shows

Favorite and Watchlist are **show-level** (you favorite *the show*,
not one episode), set from the buttons in the show header. Watched is
**per-episode**.

### Shows travel with the drive too

Just like movies, a show's personal state (favorite / watchlist /
note / per-episode watched) is mirrored into a `cinelibrary-state.json`
in the show folder. Remove the drive and re-add it later — your
progress comes right back. See *State that travels with your drives*.

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

### Sharing a list

Right-click any list in the sidebar to get two sharing actions:

- **📂 Copy movies to folder…** — copy the actual on-disk movie folders
  (videos + nfo + posters) to a destination drive. Use this when you
  want to take a list with you on a USB stick.
- **📤 Export as image…** — save a clean poster-grid PNG of the list
  with its name, count, and date. Use this to share "my top 10 heist
  movies" anywhere. Doesn't touch your video files.

---

## Tags — your own labels

Lists are great for curated buckets ("Movies for movie night"). **Tags**
are for cross-cutting labels you might apply to many movies that don't
belong together: "rewatched", "comfort", "Christmas", "long-haul flight",
"director's cut". Tags differ from lists in three ways:

- A movie can have many tags
- Tags have no order — they're just labels
- Adding a tag is one click + a name

### Adding a tag

In the movie detail dialog, look for the **🏷 + Add tag** button under
the action row. Click it, start typing — existing tags suggest as you
go. Press Enter to apply. The same flow works for TV shows on the show
page.

### Filtering by tag

Every tag you've used shows up under **🏷 TAGS** in the sidebar with a
count badge. Click any tag → the library filters to that tag. A
removable filter chip appears at the top.

You can also click a tag chip in the detail dialog to jump straight to
that filtered view.

### Removing a tag

In the detail dialog, click the **✕** on any tag chip. That movie or
show drops the tag; if no items still carry the tag, it disappears from
the sidebar.

### Tags travel with the drive

Like lists, favorites, and notes, tags are saved into the drive's
sidecar file. Plug the drive into another PC running CineLibrary and
your tags come back on the next scan.

---

## Discovery

Three places to find things to watch without browsing the whole grid.

### 🕓 Recently Watched

Sidebar entry under DISCOVER. Lists everything you've played, freshest
first. Hidden when you've never watched anything yet.

### 📅 On This Day

Sidebar entry that only appears when there's something to surface for
today. Click it for a dedicated page split into two sections:

- **🎞 You watched on this date** — movies you've played on this
  calendar date in past years.
- **🎬 Released on this date** — films that came out on this date in
  past years (cinema anniversaries — works even on a fresh install,
  with no watch history of your own).

Both fall on the *same day of the year*, regardless of year. Comes
back tomorrow with a different set.

### 🎲 Surprise me

Two flavours:

- **Sidebar "Surprise me"** — random pick from anything unwatched in
  your library. Use when you have no filter set and just want a pick.
- **🎲 button on the Library toolbar** — *filter-aware*. Honours
  whatever you're currently looking at — random comedy from the 90s,
  random movie in this list, random tagged "rewatched". If nothing
  matches the filter, you'll get a toast.

Both prefer movies on online drives so you can actually play the pick.

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

## State that travels with your drives

Your watched marks, favorites, watchlist, list memberships, and notes
are personal — they're the only things CineLibrary can't rebuild for
you by re-reading a `.nfo`. Losing them when you remove and re-add a
drive would be no fun, so as of **v2.7.0** they ride along **with the
drive itself**.

### How it works

For every movie that has any personal state (watched, favorited,
watchlisted, played at least once, sitting in one of your lists, or
carrying a note), CineLibrary writes a tiny file called
**`cinelibrary-state.json`** into that movie's folder, right next to
the `.nfo`. It looks something like this:

```json
{
  "version": 1,
  "watched": true,
  "favorite": true,
  "lastPlayedUnix": 1747576800,
  "lists": ["All Time Great"]
}
```

Three things happen automatically:

- **Every time you mark / unmark / play / list / note** a movie, the
  file gets rewritten. Best effort — if the drive is offline at the
  moment, nothing breaks; the sync will catch up on the next sweep.
- **When you scan or rescan a drive**, CineLibrary reads any
  `cinelibrary-state.json` it finds and merges the values back into
  its library. This is what makes the "remove a drive then re-add it"
  flow non-destructive.
- **On startup**, a quiet background sweep makes sure every movie
  with personal state has an up-to-date file on disk.

### The "Remove Drive" dialog

When you click 🗑 Remove Drive on the Drives page, you'll now see how
many movies on that drive have personal state attached, plus a
checkbox: **"Save that state to the drive first"** (default ON).
Leaving it ticked guarantees that re-adding the drive later restores
everything.

If the drive is **offline** when you try to remove it, the dialog
warns you that the state can't be saved and recommends cancelling
until you reconnect.

### The "Sync state" button

Each drive card on the Drives page has a **Sync state** action. Most
of the time you'll never need it — the automatic mirroring covers
day-to-day use. It's useful when:

- You want to manually push everything to disk right now (e.g. you're
  about to hand the drive to someone).
- You restored from a backup of `CineLibrary-Data\cinelibrary.db` on a
  new machine and want to make sure the drives reflect the DB.
- A bunch of edits happened while the drive was offline; this forces
  the catch-up.

### Conflict rule

If you ever end up with both a DB row and a sidecar file with
different values (e.g. you marked something watched on machine A,
then plugged the drive into machine B which already had its own
copy), the **DB wins where it already has a value**. Lists are
**additive** — joining a missing list, never silently removing.

### What this doesn't touch

`cinelibrary-state.json` is the **only** file CineLibrary writes to
your movie folder. Your `.nfo`, posters, fanart, and the video files
themselves are never modified. The sidecar file is a few hundred
bytes per movie and is safe to delete — CineLibrary will just
re-create it on the next change.

---

## Backup and restore

The sidecars give you per-drive resilience. The **Backup** entry under
**TOOLS** in the sidebar gives you whole-library resilience — one JSON
file covers every favorite, watchlist entry, note, list, tag, watched
flag, last-played time, per-episode favorite/note, and watch-history
event.

### Export a backup

Click **Backup** → **📤 Export backup…** → pick a destination
(Documents, OneDrive, anywhere). You get a single `.json` file with the
current date in its name. Save it where your usual backups live.

### Restore on a new PC (or after a wipe)

On the new install, first scan in your drives the normal way so the
catalog rebuilds. Then click **Backup** → **📥 Import backup…** → pick
the file. The summary line tells you what merged:

```
Imported: 47 movies, 6 shows, 89 episodes, 2 new lists,
4 new tags, 312 history events. Skipped: 3 (drive not mounted).
```

Anything skipped is on a drive that isn't currently plugged in —
re-import after mounting it and the rest will merge in.

### Conflict rule

Backup imports are **additive — your DB wins on conflicts**:

- Re-importing the same file is a no-op.
- A movie marked watched in either the DB or the backup ends up
  watched. Same for favorite and watchlist.
- Notes that are non-empty in the DB are preserved; otherwise the
  backup note fills in.
- `last_played_at` becomes the most recent of the two values.
- Lists and tags are union-merged (created if missing, additively
  linked).
- Watch-history events are always appended.

Nothing is ever removed by an import.

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

Click the theme button in the bottom of the sidebar to cycle between
**System → Dark → Light**. CineLibrary uses Mica backdrop on Windows 11
for a native feel and remembers your choice between sessions. You can
also set the theme from **Settings** (below).

### Collapsing the sidebar

`Ctrl+B` collapses or expands the sidebar (or click the `⮜` button at
the bottom-left). Collapsed mode gives the grid more room — useful on
small displays.

---

## Settings

Click the **⚙️ gear icon** at the bottom of the sidebar to open
**Settings**. Everything here is saved between sessions and applies
instantly — no restart.

### Theme

Light / Dark / System — the same choice as the quick theme button,
shown as a simple picker.

### Background material (Mica)

**On by default.** Shows the Windows **Mica** material behind the sidebar
and the floating content panel — a soft surface tinted by your desktop
wallpaper that gives the app a sense of depth. Turn it **off** for a
flat, solid background — handy on a lower-end GPU, or if you simply
prefer a plainer look. The change applies instantly.

### Card drop shadows

**Off by default.** Turn this on to give each movie card a soft shadow
that lifts it off the grid. It's a small touch of depth — purely
cosmetic.

Why it's off by default: a resting shadow on every card adds a little
work while you scroll. On most machines you'll never notice, but
leaving it off keeps scrolling as smooth as possible on every PC. If
you like the look and your scrolling stays buttery, turn it on and
enjoy.

### Reduce motion

**Off by default.** When on, hovering a movie card no longer zooms and
lifts it — the card stays still and just shows its quick-action
buttons. Turn this on if the hover animation bothers you, or to shave a
little extra work on lower-end graphics.

---

## Keyboard shortcuts

| Shortcut         | Action                                       |
|------------------|----------------------------------------------|
| `/`              | Focus the search box                         |
| `Ctrl+F`         | Focus the search box (alternate)             |
| `Esc`            | Clear search, then clear multi-select        |
| `F`              | Toggle favorite on the current selection     |
| `W`              | Toggle watchlist on the current selection    |
| `Delete`         | Remove selection from the current list view  |
| `Ctrl+A`         | Select every card currently visible          |
| `Ctrl+click`     | Toggle a single card into the selection      |
| `Shift+click`    | Range-select from the last clicked card      |
| `Ctrl+B`         | Toggle sidebar                               |
| `Ctrl+Q`         | Quit                                         |
| `Ctrl+Shift+/`   | Show this shortcuts list in-app              |
| `PgDn` / `PgUp`  | Scroll the movie grid one viewport up/down   |
| `Home` / `End`   | Jump to the top / bottom of the movie list   |
| `↑` / `↓`        | Scroll by one row of cards                   |

Card shortcuts (`F`, `W`, `Delete`) operate on the **current
multi-selection**. Select one or more cards first with click,
Ctrl+click, or Shift+click — then the key applies to all of them.

The card shortcuts and `/` are gated on text-edit focus, so typing
those letters into the search box still works normally.

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
