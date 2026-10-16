# Changelog

All notable changes to CineLibrary are documented here.
Format roughly follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

> Per-release notes for the 2.x and early-3.x versions between the two
> entries below live on the [Releases](https://github.com/aungkokomm/CineLibraryCS/releases) page.

## [4.6.2] - 2026-10-17

### Added
- **Leave a folder out with `.nomedia`**, as in Kodi. An empty file named
  `.nomedia` in a folder keeps that folder and everything inside it out
  of the library, movies and TV shows alike. Handy for episodes whose
  `.nfo` says `<movie>` (some old scrapers wrote those), which CineLibrary
  otherwise reads as movies, as the standard says. Anything from such a
  folder already in your library shows as missing after the next Update,
  and **Review** on the Drives page removes it.

## [4.6.1] - 2026-10-16

### Fixed
- **A show split into one entry per season becomes one show again.** When
  a show's season folders (`Dark/Dark Season 1 Complete`, ...) were each
  scraped as their own show and the show is then given one `tvshow.nfo`
  in its main folder (CineLibrary Essentials 1.3.1 does this), the old
  per-season entries no longer stay behind marked missing. Their watched
  episodes, favorite, watchlist, note, lists and tags move to the show,
  and are saved onto the drive with it.
- **Adding a folder that holds folders you already added** (for example
  `G:\Serials` after adding its shows one by one) now replaces them in
  the drive's list with that one folder, instead of keeping them all and
  scanning them twice on Update. Nothing leaves the library. Adding a
  folder that's inside one already listed says so.

## [4.6.0] - 2026-10-16

The movie window and the show page show everything CineLibrary
Essentials 1.3 writes and catch up with an edit in one click, and your
whole library can get its IMDb ratings.

### Added
- **Missing episodes on the show page.** Each season's row shows the
  episodes that have aired but aren't in your library, greyed with their
  air date, and the season's line says how many ("8 episodes · 2
  missing"). Seasons you have none of are listed under the season tabs.
  CineLibrary asks TMDb when you open a show that has a TMDb id; offline,
  the page shows what you have. Specials and episodes that haven't aired
  never count, a double-episode file counts as both, and a show kept in
  one folder per season counts every folder. **Settings → Show missing
  episodes** turns the check off, so CineLibrary stays fully offline.
- **IMDb and TMDb ratings on the show page**, each with its votes, as in
  the movie window.
- **More about a show:** Created by, every studio and network, the
  country, the tagline, the original title and how long an episode is.
- **Edition in the movie window** (Director's Cut, Extended, IMAX),
  beside the year and runtime.
- **🎞 Trailer in the movie window**, when the `.nfo` has a trailer link.
  It opens in your web browser.
- **Tags from your `.nfo` files** show after your own tags, outlined.
  Click one to see every movie with it; they're in the sidebar's Tags
  too. They change with the `.nfo` (for example in Essentials' editor),
  so they have no ✕ and stay out of your backups and state files.
- **Update IMDb ratings** on the Drives page. It gets the IMDb rating and
  votes of every movie and show (a title whose `.nfo` only has a TMDb id
  gets its IMDb id from TMDb first), makes IMDb the main ★ unless you
  untick it, and shows them at once. They're written into each `.nfo` the
  way CineLibrary Essentials' Refresh IMDb ratings does, changing only the
  ratings and a missing IMDb id: on connected drives now, on the others
  the next time you plug them in.
- **Fetched pictures go onto your drives by themselves.** After Fetch
  missing info, the poster, fanart and cast photos are copied into the
  movie's or show's folder (and a movie's details into its state file),
  only where that file isn't there yet; your `.nfo` is never changed. A
  drive that isn't connected gets them the next time you plug it in, or
  start CineLibrary with it. Turn it off with **Settings → Save fetched
  info to your drives**. Shows are new here: Sync to drive copied a
  movie's pictures only.
- **Reload from .nfo**, in the movie window and on the show page, reads
  that one folder again: the details, the pictures and a show's
  episodes. Handy after editing a title in CineLibrary Essentials,
  without updating the whole drive.

### Changed
- **IMDb ratings in gold, TMDb's in green**, in the movie window and on
  the show page, IMDb first.
- **Every studio and country**, where the movie window showed the first.
  Click any studio to see its movies. On the show page the studios moved
  from the info line to their own place, beside Created by.

The new details from your `.nfo` files (edition, every studio, tags, a
show's ratings and more) appear once at start-up, from the copies of the
`.nfo` files CineLibrary keeps, with no rescan.

## [4.5.1] - 2026-10-14

### Changed
- **The main rating is the one your `.nfo` marks as default**
  (`default="true"`), as in Kodi. Before, CineLibrary took the first
  rating listed. Ratings out of another scale (`max="100"`) count out of
  10, as in Kodi. Movies pick this up once at start-up, with no rescan;
  TV shows and episodes on their next rescan.
- **Movie window: the ratings row shows only when there's more than one
  source.** With one, it repeated the yellow ★ chip.
- **AUDIO is back** as the label of the audio track languages (4.5.0
  called it Language). It comes from the `.nfo`'s `<audio>` details,
  beside SUBTITLES.

### Fixed
- **TV show cards on Favorites, To Watch, lists and search follow the
  page's Size.** They kept the All TV Shows size, so at S, L or XL the
  shows row didn't match the movies under it.

## [4.5.0] - 2026-10-12

Favorite and Play from the card, and a movie window you can browse.

### Added
- **Favorite, Watched and Watchlist from the movie card.** Point at a
  card and three round icons appear in its top corner (📌 ✓ ♥), dim when
  off and bright when on; a click switches them. A favorite keeps its ♥
  on the card. TV show cards get the same 📌 and ♥. The hover panel keeps
  the genres and View Details (or ▶ the next episode).
- **▶ Play from the movie card.** Pointing at a movie whose drive is
  connected shows a Play button (before, only a double-click played it);
  when the drive isn't connected, the card says which drive it's on.
- **Previous / next in the movie window.** The ← and → keys, or the
  arrows at the top left of the banner, step through the movies of the
  grid or list you opened it from, in the same order and filter, without
  closing the window. It shows where you are ("12 of 340").
- **The rest of the collection.** Under the cast, "Part of the James
  Bond Collection" shows every film of it you own, in order, with a ✓ on
  the ones you've watched. Click one to see it in the same window.
- **More by the director.** Then "More by Steven Spielberg": up to 12 of
  their other films you own, newest first, with See all for the rest.
- **Writer in the movie window**, beside Director and Studio. Writers
  were already read from your `.nfo` files; a long list shows the first
  four, with the rest when you point at it.

### Changed
- **A heart for Favorites everywhere** (cards, sidebar, list view, the
  movie, show and episode windows). The star is for ratings only.
- **Movie window: genres sit under the rating, year and runtime**, as
  chips you can click, where TMDb shows them too. Director takes their
  old spot, then Writer and Studio.
- **Plainer labels**: Overview (was Plot), Language (was Audio) and
  File type (was Container).

### Fixed
- **Some short titles sat a line too high on cards**, with a blank line
  under them. The card worked out where the title wraps at its basic
  width, then drew it on the wider card the grid stretches it to, so a
  title that fit on one line still took two. Movie, TV show and
  collection cards.

## [4.4.1] - 2026-10-10

### Fixed
- **The movie window shows the whole cast**, so every actor photo you
  have appears. It stopped at the first 12 actors, which hid the photos
  of everyone after them (about half of a typical library has a bigger
  cast). A very big cast shows its first 24 with a **Show all** button.

### Changed
- **Show page: genres sit under the info line**, above the plot and your
  note, so a note no longer pushes them down.
- **Show page: your tags sit beside Add note and Fetch missing info**,
  and wrap in their own space, so adding several no longer pushes those
  buttons away from the row above.

## [4.4.0] - 2026-10-09

Collections that show your progress, and an easier-to-read episode window.

### Changed
- **Collection cards are the size of TV show cards** and fill their spot
  in the grid. They were narrower than a poster (at size M, 150 by 280),
  so the posters were cut at the sides, and they sat a little to the
  right of the page title.
- **Collection cards show how far you are**: "3 movies" on the left,
  "1/3" in purple on the right, and a purple progress bar, as on show
  cards. The green ✓ still marks a collection you've finished.
- **The episode window is easier to read**, from the sofa too. It's dark
  like the show page's header (in both themes) with a thin frame, its
  text is larger and brighter, the show's name sits on a small first line
  with the episode's name under it, and it's wider (up to 720 pixels).

## [4.3.0] - 2026-10-06

Notes for TV shows.

### Added
- **Write a note about a show.** The show page has a **📝 Add note**
  button next to Fetch missing info. The note appears under the plot as
  "Your note", and **📝 Edit note** changes it (clear the text to remove
  it). Movies and episodes had notes; shows didn't.
- **Shows with a note are on the Notes page**, in a TV shows row above
  your noted movies, and the sidebar's Notes count includes them.
- Show notes are saved like the show's favorite and watchlist: in the
  show folder's `cinelibrary-state.json` when its drive is connected, and
  in Backup.

## [4.2.0] - 2026-10-06

A movie window that uses the whole screen, CineLibrary's purple everywhere,
and two fixes.

### Changed
- **The movie details window fits your screen.** It was a column at most
  1100 pixels wide, so a 1440p or 4K screen left wide empty margins. It
  now uses the whole width and arranges itself to fit:
  - **Wide windows**: a bigger poster, with the buttons and plot beside it
    and genres, director, studio, file details and your note in a column
    of their own. Most movies fit on one screen without scrolling.
  - **Narrow windows** (a tablet held upright, or the window snapped to
    half the screen): a shorter banner, a smaller poster, and the details
    stacked one under another.
  - In between it looks as before, filling the window. The banner grows
    with the window, and the poster and banner pictures are loaded sharp
    enough for the bigger sizes.
- **CineLibrary's purple is the accent everywhere.** Windows' own accent
  colour (blue for most people) showed in the drop-down lists, the
  Settings radio buttons and switches, check boxes, links, the line under
  a text box you're typing in, and buttons such as the episode window's
  ▶ Play. They're all purple now, in both themes.

### Fixed
- **List view keeps up with the theme.** Rows the mouse had passed over
  kept the old theme's colour, so after switching to light the rows stayed
  dark with unreadable titles, and switching back left them white. The
  same went for the drive cards on the Drives page.
- **Backup and Export light up in the sidebar** while their window or menu
  is open, like the other entries, then the page you're on lights up
  again.

## [4.1.0] - 2026-10-06

A cleaner All TV Shows page, and show cards you can use without opening them.

### Added
- **Point at a show card for a quick panel**: its genres, seasons and how
  much you've watched, with **▶ Play** for the next episode (it plays
  straight away and is marked watched, as on the show page), **Favorite**
  and **Watchlist**. When the drive isn't connected, the panel says where
  the next episode is. On every page with show cards.

### Changed
- **All TV Shows is just your shows**, like All Movies. The big Continue
  Watching row at the top is gone: since 4.0.0 the Continue Watching page
  lists the shows you're partway through, so the row only repeated it and
  pushed your shows down.
- **Show cards on Continue Watching say what's next**: "Next: S01E04", the
  episode ▶ Play next on the show page starts.
- **The All TV Shows and All Movies grids line up with the page title.**
  Cards sat in the middle of grid cells that stretch to fill the row, so
  the grid started a few pixels to the right of the title, and on Continue
  Watching the movies didn't line up with the shows above them. Cards now
  fill their cells, up to the full width of the poster.

### Fixed
- **Visiting Continue Watching, Recently Added or Recently Watched no longer
  changes your movie sort.** Their own order was saved as your sort, so All
  Movies came back sorted by last watched or date added. Your sort now
  comes back when you leave those pages.
- **The sort box shows the real order on those pages.** It wasn't updated
  for them, and "Last Watched" read as "Title".

## [4.0.1] - 2026-10-05

### Added
- **The TV show page says which drive the show is on**, as the movie
  details window does: "Located on" with the drive's name, and a green dot
  and its letter while it's connected.

### Fixed
- **Fetch missing info now fills in cast photos that only lived on the
  drive.** A photo from the show's or movie's `.actors` folder showed only
  while that drive was connected, and fetching never replaced it, so some
  actors (in Breaking Bad: RJ Mitte, Betsy Brandt) kept their initials while
  the rest of the cast had photos. Run Fetch missing info once more on one
  of those titles; actors are shared, so every show and movie they're in
  gets the photo. The photo on the drive is never changed.

## [4.0.0] - 2026-10-05

TV shows join Favorites, To Watch and Continue Watching.

### Added
- **A TV shows row on Favorites, To Watch and Continue Watching**, above the
  movies, like the one in My Lists. Shows you mark as favorite or watchlist
  could be saved before, but no page listed them. Continue Watching shows the
  shows you're partway through, most recently watched first.
- **The To Watch and Continue Watching counts** in the sidebar include shows,
  and the count beside the title reads like "36 movies · 3 shows".

### Changed
- **Page titles say where you are**: Favorites, To watch, Continue watching,
  Notes, Recently added and Recently watched, instead of "All movies".
- **The TV shows row fits its cards.** Smaller cards used to float in a
  fixed-height row with an empty band above them.
- **Show cards outside All TV Shows use your TV poster size** from the start,
  instead of the default size until All TV Shows had been opened.

## [3.10.0] - 2026-10-05

### Added
- **Fetch missing info from TMDB on the TV show page**, as in the movie
  details window. It fills only what a show is missing: poster, fanart,
  plot, year, rating, certification, status, network, genres, and cast with
  photos. A show without a TMDb id is confirmed in a quick picker first.
- **IMDb and TMDb buttons on the show page.**

### Changed
- **A new look for the TV show page.** The fanart is a banner across the
  top, the poster overlaps it and the info sits below, as in the movie
  details window. The cast shows as photo cards with name and role on one
  line, and your tags sit next to Add to list. The page now ends after the
  cast, so a wide window no longer leaves a big empty area.
- **TV cast photos show with the drive offline too**, from the photo links
  in your library, as they already did for movies.
- **A rescan keeps show details the .nfo leaves blank**, so Update Database
  no longer wipes what you fetched from TMDb. Anything the .nfo has still
  wins.

### Fixed
- **Show titles in capitals** at the top of the show page. They're shown as
  written now.
- **Continue Watching cards on All TV Shows had no poster.** The poster
  loaded, but the 📺 placeholder stayed on top of it.

## [3.9.0] - 2026-10-03

### Changed
- **One-row header on All Movies, All TV Shows and Collections.** Title and
  count, the All / Unwatched / Watched pills, sort, Surprise me, grid or list
  and poster size now share one row. In a narrow window it wraps neatly.
- **Poster size is a drop-down** ("Size: M") instead of four S / M / L / XL
  buttons.
- **Export moved to Tools** in the sidebar. It offers all movies, and when
  All Movies is filtered, searched or showing a list, "This view" too.
- **Drives moved to the bottom of the sidebar**, as the first small button,
  next to Switch theme. Its tooltip shows how many drives you have.
- **Colour-coded sidebar icons**: each section (Library, Discover, Browse,
  Tools) has its own colour, in dark and light themes.
- **Tighter frame.** The gaps around the sidebar and the main page are an
  even 8 pixels, so the search box sits closer to the movies.
- **The movie count beside the title** always shows how many movies the view
  holds. It used to read "60 of 1,200" while pages were loading, which looked
  like a filter.

### Fixed
- **Export wrote only the movies loaded so far.** In a big library that could
  be the first few pages. It now writes every movie in the view.

## [3.8.0] - 2026-10-02

### Added
- **Season tabs on the show page.** Pick a season from a row of tabs and the
  page shows just that season, so reaching season 10 no longer means a long
  scroll. It opens on the first season you haven't finished; Specials come last.
- **‹ › buttons on each episode row** move a page of episodes at a time.
  Scrolling sideways still works.
- **Open Folder on the show page**, like the movie details window.
- **Certification and studio on the show page**, so the info line reads
  like "2022 ★ 7.0 TV-14 Ended 0/6 watched Syfy".
- **Collections toolbar**: sort by name, number of movies, year or date added;
  All / Unwatched / Watched; and S / M / L / XL poster sizes, remembered like
  All Movies.
- **Green watched tick** on a show card once every episode is watched, and on
  a collection card once every movie is.

### Changed
- **Collection cards look like movie cards.** The poster fills the card, with
  the name and movie count on it.

### Fixed
- **Poster size buttons on All TV Shows** turned blue when selected. They're
  purple, like the rest of the app.
- **Movie count cut off** under a two-line collection name.
- **Cast role cut off** under a long actor name in the movie details window.
  The cards now grow to fit, including with a larger Windows text size.

## [3.7.2] - 2026-09-29

### Fixed
- **Statistics page pushed off to the right** and cut off at the edge,
  most noticeably in smaller libraries. It now sits centred.
- **Scrollbar covered text and buttons in Settings.** Settings and the
  other scrolling dialogs now leave room for the scrollbar.

## [3.7.1] - 2026-09-29

### Fixed
- **Movie details window pushed off to the right** on movies without
  fanart. The page was shifted right and cut off at the edge, hiding the
  IMDb and TMDb buttons. It now sits centred like every other movie.
- **Buttons ran off the edge of the movie details window** when it was
  narrow, hiding "Add to list". The buttons, and the file info strip below
  them, now wrap onto a second line instead.

## [3.7.0] - 2026-09-29

### Added
- **All TV shows gets a toolbar** like All movies: sort by title, year,
  rating, date added or last watched; All / Unwatched / Watched; and
  S / M / L / XL poster sizes. Your choices are remembered.

### Changed
- **Roomier show page header.** More space around the poster and between the
  title, details, plot, buttons and cast, a slightly larger poster, and easier
  line spacing for the plot, which now reads as one paragraph.

### Fixed
- **Show plot sat off to the right** on wide windows. It now lines up under
  the title.

## [3.6.0] - 2026-09-29

### Added
- **Choose your video player** (Settings, Playback). Pick VLC, MPC-HC,
  PotPlayer, mpv or any other program, and every Play button opens videos
  there. "Use Windows default" goes back to the Windows default, which is also
  used automatically if the chosen program is later removed.

### Fixed
- **TV shows with season folders.** Episodes inside `Season 01`, `Season 02`,
  `Specials` and similar folders (Kodi's layout, also written by CineLibrary
  Essentials) were not found, so such shows showed 0 seasons and could not be
  played. Episodes, their `.nfo` details and thumbnails are now read from
  season folders as well as from the show folder itself. Rescan the drive to
  pick them up.
- **A floating "Esc" label** no longer appears over the movie details window
  wherever the mouse rests.
- **Uninstalling keeps your library, also after an upgrade.** 3.5.0 stopped the
  uninstaller from deleting `CineLibrary-Data`, but an install upgraded from
  3.4.x or earlier still carried the old delete step. Installing 3.6.0 removes it.

## [3.5.0] - 2026-09-25

### Changed
- **Runs on .NET 10 and Windows App SDK 2.5.1.** .NET 8 reaches end of support
  on 10 November 2026, and the Windows App SDK version used before (1.6) was
  already out of support. Everything still ships inside the portable folder, so
  there is nothing extra to install. Start-up is as quick as before and memory
  use is slightly lower.

### Fixed
- **Uninstalling no longer deletes your library.** The uninstaller removed the
  whole app folder, including `CineLibrary-Data`. It now removes only the app's
  own files and leaves your database, posters and art in place.
- **A rare error at start-up** ("Index was out of range" in
  `startup-crash.log`). The background save of watched, favorite and note state
  to your drives used the database connection without waiting its turn while
  the library and sidebar loaded. Backup export and import had the same gap,
  which could also stop an import with a "transaction" error. They now take turns.
- **Deeply nested install folders.** CineLibrary could fail to start when
  installed very deep in a folder tree. The new runtime handles long paths.
- **Dupes description** now matches how Dupes works: copies in a different
  audio language or edition are kept on purpose, copies that differ only in
  quality or codec are flagged.
- `startup-crash.log` now records the full details of an error.

## [3.4.4] - 2026-06-22

### Added
- **Dupes (Tools → Dupes).** Finds movies your library holds more than once and
  helps you reclaim the space, without touching the copies you keep on purpose.
  Films are matched by TMDb / IMDb id (else title + year), so different movies
  that share a name aren't merged. Copies that differ by **audio language** or
  **edition** are marked "kept on purpose" and never flagged; only genuine
  same-version copies are surfaced. For those, Dupes recommends a keeper (best
  resolution, then largest), shows reclaimable space per set plus a running
  total, and offers Open folder, Send to Watched & Gone, and a remembered
  "Ignore set". Non-destructive: the app never deletes files, and a deleted
  copy drops off on its own when you return to the app.

### Changed
- Resolution is read by width (1920 to 1080p) so cropped widescreen files aren't
  misjudged as a lower quality.
- Distinct sidebar icons for Dupes and Backup.

## [3.4.2] — 2026-06-22

### Improved
- **Smoother, more reliable artwork rendering across the app.** Cast photos in
  the Movie Details window, collection cover tiles and TV show cast now display
  consistently for every library — no matter how your folders are named — with
  efficient, on-demand image loading throughout the detail views.

## [3.4.1] — 2026-06-21

A focused follow-up to 3.4.0 that makes **Fetch missing info from TMDB**
actually fill in everything it should.

### Fixed
- **Cast photos now fill in reliably.** Movies whose cast came from MediaElch
  (with web-based actor thumbnails) were left with blank faces — fetched
  photos are now downloaded and cached, so they show up and keep working
  offline.
- **Genres and Director fill too.** A fetch used to fill the poster, plot and
  details but leave the Genres and Director rows blank. No longer.
- Fetched cast, genres and director are written home by **Sync to drive** and
  survive a rescan, alongside the rest of your filled-in details.
- Re-fetching a movie that already has good cast no longer reorders or
  relabels it — existing cast is left alone; only missing photos and details
  are added.

## [3.4.0] — 2026-06-17

A big update that, for the first time, lets CineLibrary reach out to **TMDb**
— always on demand, never in the background — to fill gaps and remember films
you no longer keep on disk. The app stays a fast, offline reader everywhere
else.

### Added
- **Watched & Gone — Add watched movie.** Record a film you watched but never
  had in your library (no file on any drive). Search TMDb, pick the match, and
  CineLibrary saves it as a watched **record** with poster, top-billed cast,
  plot and details — plus your **note**, **tags** and the **date you watched
  it**. Lives in Watched & Gone, fully isolated from your library.
- **Fetch missing info from TMDB.** A button on **every** Movie Details window.
  For a partly-scraped movie it fills *only the blanks* — poster, fanart, plot,
  year, runtime, cast, studio, and so on. Matches by the movie's **TMDb id**
  when the `.nfo` has one (instant), otherwise a quick pick-from-list. **Fill-
  only:** anything you or MediaElch already set is never overwritten.
- **Sync to drive (state + fetched art).** The Drives page's per-drive sync now
  also writes fetched artwork (poster / fanart / `.actors\`) into the movie's
  folder and records fetched text + cast in the `cinelibrary-state.json`
  sidecar — so a rescan reads it back and the **drive stays the source of
  truth**. Non-destructive: it only adds files that are missing and **never
  rewrites your `.nfo`**. Runs only when you click it (the automatic startup
  sync still handles personal state alone) and is disabled for offline drives.
- **Background material (Mica): Off / Subtle / Strong.** Choose how much of the
  Windows Mica/wallpaper tint shows behind the sidebar (Settings → Background
  material). The scrolling poster area always stays solid for performance.

### Changed
- Fetched cast photos and artwork are cached in the portable data folder
  (`manual_posters\`, `manual_fanart\`, `manual_actors\`) as paths relative to
  it, so they travel with your library and keep showing offline.
- Sidebar and content cards now share identical geometry — matched top/bottom
  margins and corner radius.

### Fixed
- Keyboard-shortcuts dialog showed blank key chips when the Windows theme and
  the in-app theme differed. Fixed a broader class of wrong-theme brush lookups
  across the UI at the same time.

## [2.0.1] — 2026-05-09

### Fixed
- Rare *"Execute requires the command to have a transaction object…"*
  crash during **Refresh changes**. Scanner now opens its own SQLite
  connection so its long-running transaction can't taint concurrent
  sidebar reads on the shared connection. WAL mode handles the
  multi-connection safety at the SQLite level.

### Added
- Keyboard navigation in the library view:
  - **PgDn / PgUp** scroll one viewport.
  - **Home / End** jump to top / bottom (End loads remaining pages first).
  - **↑ / ↓** scroll by one card row (grid) or one list row.
  Gated on focus — when the search box has focus, these keys do their
  normal text-editing thing.

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
