<div align="center">

# 🎬 CineLibrary

**A fast, native Windows app to browse, manage and play your movies & TV shows.**

Browse, search, and play across any number of external drives - offline, portable, no telemetry.

[![Latest release](https://img.shields.io/github/v/release/aungkokomm/CineLibraryCS?style=for-the-badge&label=latest)](https://github.com/aungkokomm/CineLibraryCS/releases/latest)
![GitHub Repo stars](https://img.shields.io/github/stars/aungkokomm/CineLibraryCS?style=for-the-badge)
![GitHub all releases](https://img.shields.io/github/downloads/aungkokomm/CineLibraryCS/total?style=for-the-badge)
![License](https://img.shields.io/github/license/aungkokomm/CineLibraryCS?style=for-the-badge)









</div>

> ***Stop letting your movies gather dust in forgotten directories.***
> CineLibrary makes your whole collection visible, searchable, and alive again.

**A constantly evolving passion project** - check the [latest release](https://github.com/aungkokomm/CineLibraryCS/releases/latest) for what's new.

---

## What is CineLibrary?

CineLibrary turns your movie and TV folders into a beautiful library you can browse, manage and play, across as many external drives as you own. The built-in player remembers where you stopped, your audio and your subtitles.

Unplug a drive and its titles stay visible, ready for when it comes back. It's free and open source, runs from any folder, and needs no account.

It reads standard Kodi-style `.nfo` files and artwork. Folders not ready yet? [CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials) renames, organizes and scrapes them for you.

---

## 📸 Screenshots

Your whole collection at a glance: the sidebar, the search, and every filter and sort on one tidy row.

<img width="960" alt="CineLibrary in dark theme: the All Movies poster grid, the sidebar and the title-bar search" src="docs/images/cinelibrary-library.png" />

🖥️ **Fold the sidebar to a slim strip of icons** and the posters get the whole screen.

<img width="960" alt="CineLibrary with the sidebar folded to a strip of icons, the All Movies poster grid filling the window" src="docs/images/cinelibrary-library-folded.png" />

▶️ **Press Play and the film opens in CineLibrary's own player**, with a glossy purple control bar that fades away while you watch.

<img width="960" alt="The CineLibrary player showing a scene from Sintel, with the purple control bar: the seek bar, Play between Previous and Next, skip back 10 and forward 30, volume and the time the film ends" src="docs/images/cinelibrary-player.png" />

<sub>Player pictures on this page: *Sintel* © Blender Foundation, [durian.blender.org](https://durian.blender.org/), under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).</sub>

⏯️ **Continue Watching keeps everything you've started in one place**: films and shows together, grouped by when you last watched them, with how much is left.

<img width="960" alt="The Continue Watching page: films and TV shows together under This week and This month, each with how much is left" src="docs/images/cinelibrary-continue-watching.png" />

✨ **Richer, deeper movie details than ever before** - discover the stories behind your collection in a whole new way.

<img width="960" alt="A movie page in CineLibrary: the fanart banner, the poster, the rating and file details, Play and the other buttons" src="docs/images/cinelibrary-movie.png" />

📺 **TV shows get a page of their own**: the fanart, the cast with photos, the drive the show is on, and every episode in a list with Favorite and Watched buttons.

<img width="760" alt="A TV show page in CineLibrary: the Game of Thrones fanart banner, the poster with its IMDb and TMDb ratings under it, details, Play next and the other buttons, the cast, the season buttons and the first episode" src="docs/images/cinelibrary-tv-show.png" />

---

## 🎬 The player

Films and TV episodes play in CineLibrary's own highly customized, lightweight player, built on VLC's playback engine. Carefully trimmed of unnecessary components such as streaming, recording and visualization, it's optimized for CineLibrary's local playback experience while retaining broad format support and hardware decoding.

- **Opens in full screen** - or over the CineLibrary window, if you turn off **Open the player in full screen** in Settings.
- **Picks up where you stopped**, with the audio and subtitle tracks you chose last time.
- **Subtitles from your folders** - files next to the video, or in a `Sub`, `Subs` or `Subtitles` folder, appear in the Subtitles menu with their language. **Add subtitle file...** picks any other.
- **Skip back 10 seconds or forward 30** (or as far as you like, set in Settings), jump between a film's chapters, and go straight on to the next episode.
- **Media keys** - the keyboard's play/pause, next and previous keys, a remote's buttons and Bluetooth headphones' work the player even when another window is in front, and Windows' media panel shows the film and its poster.
- **Clear voices** - press V for an equalizer that brings voices up and the rumble of explosions down, for the evening.
- **A drive that drops out** - if the file stops answering (a drive asleep or unplugged, the computer back from sleep), the player opens it again where it was instead of closing.
- **Pause and see what you're watching** - the title, plot, director and cast fade in over the picture.
- **Play counts** - a film you watch to the end counts as played, and its page shows how many times. Counts in your `.nfo` files are read too.
- **Mouse and keyboard** - the wheel changes the volume, Space or K pauses, the arrows (or J and L) seek and change the volume, F goes full screen, A and S switch the audio and subtitles, C shows the chapters, - and + change the speed, I skips the intro, G and H move the subtitles earlier or later, and the mouse's back button closes the player.
- **Your own player instead** - turn off **Play with CineLibrary Player** in Settings and VLC, MPC-HC, PotPlayer or mpv opens your films.

<img width="960" alt="The player paused on Sintel: the title, year, length, genres, plot, director and cast over the picture" src="docs/images/cinelibrary-pause.png" />

<img width="960" alt="The player's Subtitles menu: the film's own tracks, the subtitle files from its Subs folder with their languages, and Add subtitle file" src="docs/images/cinelibrary-subtitles.png" />

---

## ✨ Highlights

- **▶️ A built-in player** - films and episodes play right inside the app and pick up where you stopped. [More about the player](#-the-player).
- **⏯️ Continue Watching** - everything you've started, films and shows together, grouped into This week, This month and Earlier.
- **📺 Movies & TV shows** - full show pages with seasons, episodes, IMDb and TMDb ratings, the aired episodes you're missing, per-episode watched/favorite/notes, and "▶ Play next unwatched."
- **💾 Multi-drive, offline-aware** - index any number of external drives; offline ones stay visible with posters intact, and your watched state, favorites and notes travel with each drive.
- **🏷️ Your own organization** - favorites, To Watch, notes, free-form tags and custom lists, with fast search and filters by genre, decade, rating, collection and watched status.
- **🧭 Discovery** - Recently Watched, Recently Added, On This Day, a filter-aware Surprise Me, and **Watched & Gone**, a memory keeper for films you watched then deleted.
- **🩹 Fill the gaps** - fetch a partly-scraped title's missing info from TMDB, get IMDb ratings for your whole library in one click, and copy fetched art onto your drives. Fill-only: your existing data is never overwritten.
- **🧹 Keep it tidy** - find duplicates with reclaimable-space totals, and back up all your personal state to one portable file.
- **🚀 Native & quick** - cold start under a second; smooth scrolling through thousands of posters.

> Full per-version notes live on the [**Releases**](https://github.com/aungkokomm/CineLibraryCS/releases) page.

---

## ⬇️ Download & install

1. Grab the latest **[portable installer](https://github.com/aungkokomm/CineLibraryCS/releases/latest)** - `CineLibrary-vX.Y.Z-Portable-Setup.exe`.
2. Run it and pick any folder - Desktop, external drive, USB stick. CineLibrary is fully portable; its data lives in `CineLibrary-Data\` right next to the exe.

Move the whole folder to another PC anytime - your library comes with it.

**Requirements:** Windows 10 (build 19041) or Windows 11 · 64-bit.

📖 New here? The **[User Guide](docs/USER_GUIDE.md)** takes you from a fresh install to thousands of cataloged titles in about five minutes.
📖 မြန်မာဘာသာ - **[မြန်မာ လက်စွဲ](docs/USER_GUIDE_MM.md)**။

---

## 🚀 Getting started

1. **Get your folders ready.** **[CineLibrary Essentials](https://github.com/aungkokomm/CineLibraryEssentials)** renames, organizes and scrapes them in a few clicks, and CineLibrary shows everything it writes. Folders already have Kodi-style `.nfo` files and artwork? You're ready.
2. **Add them.** In CineLibrary, open **Drives** → **Add folder**. It reads the `.nfo` files, caches the artwork and keeps a local index.
3. **Browse, play, unplug.** Disconnect a drive and its titles stay, marked **OFFLINE**, with posters and details intact.

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

---

## 🛠️ Build from source

**Requirements:** Windows 10 20H1+ (19041) / Windows 11 · Visual Studio 2026+ with **.NET desktop development** and **Windows App SDK** workloads · .NET 10 SDK.

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

The built-in player uses [LibVLC](https://www.videolan.org/vlc/libvlc.html) and [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) by VideoLAN and the VLC authors, under the LGPL, with FFmpeg, FreeType and other libraries. Their licences and source links are in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

IMDb ratings come from [Agregarr](https://agregarr.org/docs/imdb-ratings/)'s free ratings service.

It will always remain free - no subscriptions, no paywalls, no telemetry. Built for myself, shared with you.

---

## 🙌 Contributors

- **[jo-el414](https://github.com/jo-el414)** - daily tester; the bug reports, screenshots and ideas have shaped much of this app.

<p align="center"><sub>MIT licensed · © 2026 Aung Ko Ko · <a href="https://aungkokomm.github.io/">more of my apps</a></sub></p>
