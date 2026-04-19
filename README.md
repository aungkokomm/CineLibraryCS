# CineLibrary

A fast, native Windows movie catalog for MediaElch-scraped collections. Browse, search and play your movies across multiple external drives.

Built with C# + WinUI 3.
<img width="959" height="508" alt="image" src="https://github.com/user-attachments/assets/9c11fe12-b624-4ba1-a6d7-09564bb4e950" />

## Features

- **Multi-drive library** — index movies across external drives; knows which are currently online
- **MediaElch-compatible** — reads `movie.nfo`, posters, fanart; caches artwork locally so offline drives still show thumbnails
- **Grid + list views** — with S/M/L/XL thumbnail sizes (like Windows Explorer)
- **Full-text search** — title, actor, director, plot
- **Filters** — by drive, genre, collection, favorites, watched/unwatched
- **Collapsible sidebar** with drives, collections, top genres
- **Resizable movie detail window** — hero fanart, poster, chips, plot, cast
- **Export** — CSV or HTML
- **Keyboard shortcuts** — `Ctrl+F` search, `Ctrl+B` toggle sidebar, `Esc` clear search
- **Light / Dark / System themes** with Mica backdrop

## Download

Grab the latest portable build from [Releases](https://github.com/aungkokomm/CineLibraryCS/releases). Unzip anywhere and run `CineLibrary.exe` — no install required.

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

## License

MIT
