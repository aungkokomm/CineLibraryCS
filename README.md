# CineLibrary

A fast, native Windows movie catalog for MediaElch-scraped collections. Browse, search and play your movies across multiple external drives.

***Stop letting your movies gather dust in forgotten directories.***  
***Treat them with CineLibrary — making your collection visible, searchable, and alive again.***  
***CineLibrary: where your movies stop hiding and start shining.***


(This is constantly evolving my personal passion project, so always be sure to check latest releases)

Stay tuned, Next Release is planned for next week! 

Built with C# + WinUI 3.
<img width="960" height="510" alt="image" src="https://github.com/user-attachments/assets/498599c9-6769-417e-9e1e-117bdc54b9ee" />

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

## In next release
Will fix UI inconsistencies
   

## License

MIT
