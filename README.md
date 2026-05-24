# CineLibrary

A fast, native Windows catalog for your MediaElch-scraped **movies and TV shows**. Browse, search and play across multiple external drives.

***Stop letting your movies gather dust in forgotten directories.***  
***Treat them with CineLibrary — making your collection visible, searchable, and alive again.***  

***CineLibrary: where your movies stop hiding and start shining.***


**Constantly evolving passion project! Always check the latest releases for updates.**

Built with C# + WinUI 3.
<img width="960" height="511" alt="image" src="https://github.com/user-attachments/assets/3007115a-36ff-441b-bda3-5411ad550ecd" />



✨ **Richer, deeper movie details than ever before** — discover the stories behind your collection in a whole new way.
<img width="960" height="457" alt="image" src="https://github.com/user-attachments/assets/bc89a995-5aea-4bc1-864e-6188b9df28ff" />



![GitHub Repo stars](https://img.shields.io/github/stars/aungkokomm/CineLibraryCS?style=for-the-badge)
![GitHub all releases](https://img.shields.io/github/downloads/aungkokomm/CineLibraryCS/total?style=for-the-badge)
![GitHub License](https://img.shields.io/github/license/aungkokomm/CineLibraryCS?style=for-the-badge)


## What's new in v2.9 🎬

- **🟣 Continue Watching for TV** — a row at the top of the TV Shows page showing the next unwatched episode for every show you've started.
- **🕓 Recently Watched** — new sidebar entry that lists everything you've played, freshest first.
- **📅 On This Day** — a dedicated page that surfaces movies you watched on this calendar date in past years *plus* movies that were released on this date in past years (cinema anniversaries).
- **🏷 Free-form tags** — tag movies and shows with anything ("rewatched", "comfort", "Christmas", "long-haul flight"). Tags appear in the sidebar and filter the library with one click.
- **★ Per-episode favorite & note** — mark individual episodes as favorites and jot a note in the episode dialog.
- **🎲 Filter-aware Surprise Me** — new dice button on the Library toolbar picks a random movie from the *current* filtered view.
- **🔍 TV in global search** — the search box now matches TV shows alongside movies.
- **💾 Backup / restore** — one-click JSON export of every favorite, watchlist entry, note, list, tag, watched flag, and watch-history event. Importable on another PC to merge your state in.
- **📤 Export a list as image** — right-click any list → save a clean PNG poster grid you can share.
- **⌨️ More keyboard shortcuts** — `/` focus search, `F` favorite, `W` watchlist, `Delete` remove from current list.
- **⚡ Faster across the board** — full code-quality + performance pass; new DB indexes, debounced sidebar refresh, leaner image cache. Big libraries feel snappier.

## Features

- **📺 TV Shows** — folders with `tvshow.nfo` are detected as shows. Each show has a single page: poster, plot, cast, season-by-season episode rows, per-episode watched / favorite / note tracking, a "▶ Play next unwatched" button, and a watched-progress roll-up. Personal state travels with the drive just like movies.
- **Multi-drive library** — index movies and shows across external drives; knows which are currently online
- **MediaElch-compatible** — reads `movie.nfo` / `tvshow.nfo` + episode `.nfo`, posters, fanart, episode thumbnails; caches artwork locally so offline drives still show thumbnails
- **Grid + list views** — with S/M/L/XL thumbnail sizes (like Windows Explorer)
- **Full-text search** — title, actor, director, plot — now matching TV shows too
- **Filters** — by drive, genre, collection, decade, rating, favorites, watched, notes, tags
- **Collapsible sidebar** with drives, collections, lists, and tags
- **Resizable movie detail window** — hero fanart, poster, chips, plot, cast, tags
- **Export** — CSV, HTML, or as a poster-grid image
- **Keyboard shortcuts** — `/` focus search, `F` favorite, `W` watchlist, `Esc` clear, `Ctrl+F` search, `Ctrl+B` toggle sidebar
- **Light / Dark / System themes** with Mica backdrop
- **Continue Watching** for both movies and TV
- **Recently Watched, Recently Added, On This Day, Surprise me**
- **Notes in the movie detail dialog and per-episode notes for TV**
- **My Lists** — group movies and shows your way. Copy a list to a folder, or export as an image
- **Backup / restore** of all personal state to a JSON file

## Download

> ⚠️ **Prerequisite:** Your movies must already be organised in folders and scraped with [MediaElch](https://mediaelch.github.io/mediaelch-doc/) or [CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials) before using CineLibrary. MediaElch writes the `.nfo` metadata files and poster/fanart images that CineLibrary reads. 


Grab the latest portable build from [Releases](https://github.com/aungkokomm/CineLibraryCS/releases). Unzip anywhere and run `CineLibrary.exe` — no install required.

📖 **New here?** The [User Guide](docs/USER_GUIDE.md) walks you from a fresh install to running a catalog of thousands of movies in under five minutes.

📖 **မြန်မာဘာသာ?** [မြန်မာဘာသာ လက်စွဲ](docs/USER_GUIDE_MM.md) တွင် ဖတ်ရှုနိုင်သည်။



## Build from source

Requirements:

- Windows 10 20H1+ (19041) or Windows 11
- Visual Studio 2022+ with **.NET desktop development** and **Windows App SDK** workloads
- .NET 8 SDK

```powershell
git clone https://github.com/aungkokomm/CineLibraryCS.git
cd CineLibrary
msbuild CineLibraryCS.csproj -t:Build -p:Configuration=Debug -p:Platform=x64
```

To produce a self-contained portable build:

```powershell
msbuild CineLibraryCS.csproj -t:Publish -p:Configuration=Release -p:Platform=x64 -p:PublishDir=publish\
```

## How it works

1. Use **MediaElch** (or any tool that produces Kodi-style `movie.nfo` + `poster.jpg` / `fanart.jpg`) to scrape each movie folder on your drives.
2. Launch CineLibrary → **Drives** → **Scan** (or pick a folder on that drive).
3. CineLibrary reads the nfo files, caches the posters, and writes everything to a local SQLite index.
4. Disconnect the drive — the movies still appear (marked OFFLINE) with posters and metadata intact.

## Data location

- **Dev build**: `CineLibrary-Data/` next to the `.csproj`
- **Portable build**: `CineLibrary-Data/` next to `CineLibrary.exe`
SQLite DB + cached artwork live there. Back it up to keep your favorites/watched state across machines.

## License & Commitment
CineLibrary is free and open source software.  
It will always remain free, no subscriptions, no paywalls.  
Built for myself, shared with you.

