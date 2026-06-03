<div align="center">

# 🎬 CineLibrary

**A fast, native Windows catalog for your MediaElch-scraped movies & TV shows.**

Browse, search, and play across any number of external drives — offline, portable, no telemetry.

[![Latest release](https://img.shields.io/github/v/release/aungkokomm/CineLibraryCS?style=for-the-badge&label=latest)](https://github.com/aungkokomm/CineLibraryCS/releases/latest)
![GitHub Repo stars](https://img.shields.io/github/stars/aungkokomm/CineLibraryCS?style=for-the-badge)
![GitHub all releases](https://img.shields.io/github/downloads/aungkokomm/CineLibraryCS/total?style=for-the-badge)
![License](https://img.shields.io/github/license/aungkokomm/CineLibraryCS?style=for-the-badge)

<img width="960" height="511" alt="image" src="https://github.com/user-attachments/assets/abfa7f1c-b5c7-419c-b4bd-7224b8fc82d7" />


</div>

> ***Stop letting your movies gather dust in forgotten directories.***
> CineLibrary makes your whole collection visible, searchable, and alive again.

**A constantly evolving passion project** — check the [latest release](https://github.com/aungkokomm/CineLibraryCS/releases/latest) for what's new.

---

## What is CineLibrary?

CineLibrary is a native Windows app (C# + WinUI 3) that turns your already-organized movie and TV folders into a clean, fast, browsable catalog — posters, rich details, search, and playback — across as many external drives as you own.

Unplug a drive and its titles don't vanish; they fade to **offline** (posters and metadata intact) and wake up when you plug it back in. It's **free and open-source**, runs **portably** from any folder, and asks for nothing — no account, no subscription, no telemetry.

CineLibrary is a **reader, not a scraper**: it expects folders that already carry the standard Kodi-style `.nfo` metadata and poster/fanart images (the kind [MediaElch](https://mediaelch.github.io/mediaelch-doc/) or [CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials) produce). That focus is what keeps it fast and non-destructive.

---

## 📸 Screenshots

✨ **Richer, deeper movie details than ever before** — discover the stories behind your collection in a whole new way.

<img width="960" height="457" alt="CineLibrary — movie detail view" src="https://github.com/user-attachments/assets/bc89a995-5aea-4bc1-864e-6188b9df28ff" />

---

## ✨ Highlights

- **📺 Movies & TV shows** — full show pages with seasons, episodes, per-episode watched/favorite/notes, and "▶ Play next unwatched."
- **💾 Multi-drive, offline-aware** — index any number of external drives; offline ones stay visible with posters intact.
- **🔎 Fast search & filters** — title, actor, director, plot; filter by genre, decade, rating, collection, tags, watched status.
- **🏷️ Your own organization** — favorites, watchlist, notes, free-form tags, and custom lists.
- **🧭 Discovery** — Continue Watching, Recently Watched, Recently Added, On This Day, and a filter-aware Surprise Me.
- **💾 Backup & restore** — export all personal state (favorites, notes, lists, tags, history) to one portable JSON file.
- **🪪 State travels with the drive** — watched/favorite/notes/tags live next to each title, so they survive a drive move to another PC.
- **⚙️ Settings** — Light/Dark/System theme, optional card shadows, reduce-motion.
- **🚀 Native & quick** — cold start under a second; smooth scrolling through thousands of posters.

> Full per-version notes live on the [**Releases**](https://github.com/aungkokomm/CineLibraryCS/releases) page.

---

## 📋 Before you start

> ⚠️ **CineLibrary reads metadata — it does not create it.** Your movie and TV folders must already be organized and scraped (each title in its own folder with a `.nfo` file and poster). If they aren't yet, use **[CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials)** or **[MediaElch](https://mediaelch.github.io/mediaelch-doc/)** first — otherwise CineLibrary will have nothing to show.

<details>
<summary><b>Expected folder layout (click to expand)</b></summary>

**Movies**

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

**TV shows**

```
H:\TV\Dark\
├── tvshow.nfo                       ← show metadata
├── poster.jpg   fanart.jpg
├── .actors\                         ← cast thumbnails
├── Dark - S01E01 - Secrets.mkv
├── Dark - S01E01 - Secrets.nfo
├── Dark - S01E01 - Secrets-thumb.jpg
├── Dark - S01E02 - Lies.mkv
└── …
```

</details>

**Requirements:** Windows 10 (build 19041) or Windows 11 · 64-bit.

---

## ⬇️ Download & install

1. Grab the latest **[portable installer](https://github.com/aungkokomm/CineLibraryCS/releases/latest)** — `CineLibrary-vX.Y.Z-Portable-Setup.exe`.
2. Run it and pick any folder — Desktop, external drive, USB stick. CineLibrary is fully portable; its data lives in `CineLibrary-Data\` right next to the exe.
3. Launch **CineLibrary** → open **Drives** → **Add folder**, point it at your movie/TV root, and let it scan.

Move the whole folder to another PC anytime — your library comes with it.

📖 New here? The **[User Guide](docs/USER_GUIDE.md)** takes you from a fresh install to thousands of cataloged titles in about five minutes.
📖 မြန်မာဘာသာ — **[မြန်မာ လက်စွဲ](docs/USER_GUIDE_MM.md)**။

---

## 🧩 Companion app — CineLibrary Essentials

Folders not tidy yet? **[CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials)** renames, organizes, and scrapes rich Kodi/Plex/Jellyfin-ready metadata in a few clicks. The recommended flow:

1. **Tidy & scrape** with CineLibrary Essentials (or MediaElch).
2. **Point CineLibrary** at the cleaned folders.
3. **Browse, search, and watch.**

---

## ⚙️ How it works

1. Scrape each folder with MediaElch / CineLibrary Essentials (Kodi-style `.nfo` + `poster.jpg` / `fanart.jpg`).
2. Launch CineLibrary → **Drives** → **Add folder**.
3. CineLibrary reads the `.nfo` files, caches the artwork, and writes everything to a local SQLite index.
4. Disconnect the drive — titles still appear (marked **OFFLINE**) with posters and metadata intact.

---

## 🛠️ Build from source

**Requirements:** Windows 10 20H1+ (19041) / Windows 11 · Visual Studio 2022+ with **.NET desktop development** and **Windows App SDK** workloads · .NET 8 SDK.

```powershell
git clone https://github.com/aungkokomm/CineLibraryCS.git
cd CineLibraryCS
msbuild CineLibraryCS.csproj -t:Build -p:Configuration=Debug -p:Platform=x64
```

Self-contained portable build:

```powershell
msbuild CineLibraryCS.csproj -t:Publish -p:Configuration=Release -p:Platform=x64 -p:PublishDir=publish\
```

---

## 📂 Where your data lives

- **Dev build:** `CineLibrary-Data\` next to the `.csproj`.
- **Portable build:** `CineLibrary-Data\` next to `CineLibrary.exe`.

The SQLite database and cached artwork live there. Back it up to carry your favorites and watched state across machines (or use **Settings → Backup**).

---

## 📜 License & commitment

CineLibrary is **free and open-source** software, released under the MIT License.

It will always remain free — no subscriptions, no paywalls, no telemetry. Built for myself, shared with you.
