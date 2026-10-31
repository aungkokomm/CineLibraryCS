using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace CineLibraryCS.Services.Playback;

/// <summary>v4.10.0: what can play after the current item ends (the next episode).</summary>
internal sealed record NextUp(string Path, string Title, string Label, object? Tag, long StartMs, TrackChoice? Choice = null);

/// <summary>
/// v4.10.0: videos playing in an owned player window, one after another (Up Next). Knows
/// nothing about the library: each item carries the caller's tag, and the session reports
/// where it stopped; the caller decides what that means.
/// </summary>
internal sealed class PlayerSession
{
    private const int SeekStep = 10_000, BigSeekStep = 60_000, VolumeStep = 5, UpNextSeconds = 10;
    private const int SubtitleStepMs = 100;
    private const int NormalSpeed = 2, FitZoom = 0, FillZoom = 1;
    private static readonly float[] Speeds = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f];
    private static readonly string[] Zooms = ["Fit", "Fill", "16:9", "4:3"];

    private readonly LibVLC _vlc;
    private readonly PlayerWindow _window;
    private readonly PlayerControls _controls;
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _hide, _tick, _click, _countdown, _noteTimer, _cursor, _health, _noteHide, _pauseWait;
    private Media _media;
    private long _time, _length;
    private bool _barWanted, _closing, _released, _itemEnded;
    private NextUp? _offered, _previousItem, _nextItem;
    private Func<object?, NextUp?>? _findPrevious, _findNext;
    private PlayerMenu? _menu;
    private PlayerLabel? _note, _tip, _titleStrip;
    private PlayerPause? _pause;
    private TrackChoice? _choice;
    private bool _choicePending = true;
    private string _title;
    private bool _chapters;
    private ChapterDescription[]? _chapterList;
    private string _path;
    private int _speed = NormalSpeed, _zoom = FitZoom;
    private bool _findingSubtitles;
    private IReadOnlyList<SubtitleHit> _foundSubtitles = [];
    private int _secondsLeft;
    private int _shownPictures, _lostPictures;

    public MediaPlayer Player { get; }
    public IntPtr Hwnd => _window.Hwnd;
    internal IntPtr ControlsHwnd => _controls.Hwnd;
    internal bool ControlsShown => _controls.Shown;
    internal PlayerWindow Window => _window;

    /// <summary>The caller's tag of the item playing now.</summary>
    public object? Tag { get; private set; }

    /// <summary>The last position VLC reported (ms); safe to read after closing.</summary>
    public long Position => Interlocked.Read(ref _time);
    public long Length => Interlocked.Read(ref _length);
    public int Volume { get; private set; }
    public bool IsPlaying => !_released && Player.IsPlaying;

    /// <summary>VLC couldn't play the item at all: nothing was ever shown.</summary>
    public bool FailedToStart { get; private set; }

    /// <summary>Once per item, when it stops (closed, ended, or followed by the next one): its tag,
    /// the last position and the length (ms).</summary>
    public event Action<object?, long, long>? ItemEnded;
    /// <summary>A next item started in the same window.</summary>
    public event Action<object?>? ItemStarted;
    /// <summary>Raised once, after playback stopped and the windows are gone.</summary>
    public event Action? Closed;
    /// <summary>A track was chosen or the subtitles were moved: the item's tag and the choice to keep.</summary>
    public event Action<object?, TrackChoice>? ChoiceChanged;
    /// <summary>What the pause screen shows for an item; null shows none.</summary>
    public Func<object?, PauseInfo?>? FindPauseInfo { get; set; }
    /// <summary>What to look for subtitles online by; null goes by the title alone.</summary>
    public Func<object?, SubtitleFilm?>? FindSubtitleFilm { get; set; }

    private PlayerSession(LibVLC vlc, PlayerWindow window, string path, string title, object? tag, long startMs, int volume, TrackChoice? choice)
    {
        _vlc = vlc;
        _window = window;
        _ui = DispatcherQueue.GetForCurrentThread();
        _title = title;
        _path = path;
        _choice = choice;
        Tag = tag;
        Volume = volume;
        Player = new MediaPlayer(vlc) { EnableMouseInput = false, EnableKeyInput = false, Hwnd = window.Hwnd };
        _media = NewMedia(path, startMs, choice);
        _controls = new PlayerControls(window.Hwnd);

        _hide = Timer(3000, HideBar);
        _tick = Timer(250, Refresh, repeating: true);
        _click = Timer(PlayerWindow.DoubleClickTime, TogglePause);
        _countdown = Timer(1000, CountDown, repeating: true);
        _noteTimer = Timer(8000, () => { if (_controls.Row == "message" && _offered == null) _controls.HideRow(); });
        _cursor = Timer(3000, () => { if (!_barWanted && Player.IsPlaying) _window.SetCursorHidden(true); });
        _health = Timer(2000, LogHealth, repeating: true);
        _noteHide = Timer(1500, () => _note?.HideText());
        _pauseWait = Timer(4000, ShowPause);

        window.CloseRequested += () => _ = CloseAsync();
        window.KeyDown += OnKey;
        window.MouseMoved += OnMouseMoved;
        window.Clicked += OnClicked;
        window.DoubleClicked += OnDoubleClicked;
        window.RightClicked += ShowContextMenu;
        window.Wheel += OnWheel;
        window.PlacementChanged += () =>
        {
            PlaceBar();
            if (_zoom == FillZoom) ApplyZoom();
        };

        _controls.PlayPause += TogglePause;
        _controls.Back += () => SeekBy(-SeekStep);
        _controls.Forward += () => SeekBy(SeekStep);
        _controls.Mute += ToggleMute;
        _controls.FullScreen += ToggleFullScreen;
        _controls.CloseClicked += () => _ = CloseAsync();
        _controls.Audio += () => ShowTracks(audio: true);
        _controls.Subtitles += () => ShowTracks(audio: false);
        _controls.Previous += GoPrevious;
        _controls.Next += GoNext;
        _controls.Seek += seconds => Player.Time = (long)(seconds * 1000);
        _controls.Volume += SetVolume;
        _controls.SeekWheel += notches => SeekBy(notches * SeekStep);
        _controls.VolumeWheel += notches => ChangeVolume(notches * VolumeStep);
        _controls.SeekHover += OnSeekHover;
        _controls.Released += window.TakeFocus;
        _controls.Moved += PlaceBar;

        // VLC raises its events on its own threads, and must not be called back from them
        Player.TimeChanged += (_, e) => Interlocked.Exchange(ref _time, e.Time);
        Player.LengthChanged += (_, e) => Interlocked.Exchange(ref _length, e.Length);
        Player.Playing += (_, _) => _ui.Post(() =>
        {
            if (_released) return;
            Player.Volume = Volume;
            if (_speed != NormalSpeed) Player.SetRate(Speeds[_speed]);
            if (_zoom != FitZoom) ApplyZoom();
            if (_choicePending) ApplyChoice();
        });
        Player.EndReached += (_, _) => _ui.Post(OnEnded);
        Player.EncounteredError += (_, _) => _ui.Post(OnEnded);
    }

    /// <summary>
    /// Opens the player window over the given rectangle (physical pixels), maximized to
    /// cover a maximized owner, and starts playing at <paramref name="startMs"/>.
    /// </summary>
    public static async Task<(PlayerSession? Session, string? Error)> OpenAsync(IntPtr owner, int x, int y, int width, int height, bool maximized,
                                                                              string path, string title, object? tag, long startMs, int volume,
                                                                              Action<MediaPlayer>? beforePlay = null, TrackChoice? choice = null)
    {
        var (vlc, error) = await VideoEngine.GetAsync();
        if (vlc is null) return (null, error);
        var window = new PlayerWindow(owner, x, y, width, height, title);
        PlayerLog.Start($"open {path} | {width}x{height} at scale {window.Scale}{(maximized ? ", maximized" : "")} | from {PlayerControls.Format(startMs)}");
        var session = new PlayerSession(vlc, window, path, title, tag, startMs, volume, choice);
        window.Show(maximized);
        beforePlay?.Invoke(session.Player);
        session.Player.Play(session._media);
        session._health.Start();
        session.ShowBar();
        if (startMs > 0) session.ShowResumed(startMs);
        return (session, null);
    }

    // A path, not a URI: VLC makes the URI itself, so '#', '%' and any script in names are safe
    private Media NewMedia(string path, long startMs, TrackChoice? choice)
    {
        var media = new Media(_vlc, path, FromType.FromPath);
        if (startMs > 0) media.AddOption(":start-time=" + (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        // From the first sound, not a moment after the film started in another language
        if (choice?.Audio is { } audio) media.AddOption(":audio-language=" + audio);
        return media;
    }

    /// <summary>Once the tracks are known: the chosen ones, when the file has them.</summary>
    private void ApplyChoice()
    {
        _choicePending = false;
        Player.SetSpuDelay((_choice?.SubtitleDelayMs ?? 0) * 1000L);
        if (_choice is not { } c) return;
        if (c.Audio is { } audio && Pick(Player.AudioTrackDescription, audio, c.AudioIndex) is { } a && a != Player.AudioTrack)
            Player.SetAudioTrack(a);
        if (c.SubtitlesOff)
        {
            if (Player.Spu != -1) Player.SetSpu(-1);
        }
        else if (c.Subtitles is { } subtitles && Pick(Player.SpuDescription, subtitles, c.SubtitleIndex) is { } s && s != Player.Spu)
            Player.SetSpu(s);
        PlayerLog.Write($"tracks: audio {Player.AudioTrack}, subtitles {Player.Spu}, wanted {c}");
    }

    private static int? Pick(TrackDescription[]? tracks, string language, int index)
    {
        var same = (tracks ?? []).Where(t => t.Id != -1 && string.Equals(PlayerMenu.Language(t.Name), language, StringComparison.OrdinalIgnoreCase)).ToList();
        return same.Count == 0 ? null : same[Math.Min(index, same.Count - 1)].Id;
    }

    private void ShowResumed(long startMs)
    {
        _controls.ShowRow("message", $"Resumed at {PlayerControls.Format(startMs)}", [("Start over", false, StartOver)]);
        _noteTimer.Stop();
        _noteTimer.Start();
    }

    private void StartOver()
    {
        Player.Time = 0;
        _controls.HideRow();
        Feedback("From the start");
    }

    /// <summary>
    /// What comes before and after an item (the previous and next episode), asked at the
    /// start of each one. Without them Previous and Next go through the film's chapters.
    /// </summary>
    public void SetNeighbours(Func<object?, NextUp?>? previous, Func<object?, NextUp?>? next)
    {
        _findPrevious = previous;
        _findNext = next;
        UpdateSkip();
    }

    private void UpdateSkip()
    {
        _previousItem = _findPrevious?.Invoke(Tag);
        _nextItem = _findNext?.Invoke(Tag);
        _chapters = false;
        ApplySkip();
    }

    private void ApplySkip()
    {
        if (_previousItem != null || _nextItem != null)
            _controls.SetSkip(_previousItem is { } p ? $"Previous: {p.Label} (P)" : null, _nextItem is { } n ? $"Next: {n.Label} (N)" : null);
        else if (_chapters)
            _controls.SetSkip("Previous chapter (P)", "Next chapter (N)");
        else
            _controls.SetSkip(null, null);
    }

    private void GoPrevious()
    {
        if (_previousItem is { } previous) SwitchTo(previous);
        else if (_chapters)
        {
            var chapter = Player.Chapter - 1;
            Player.PreviousChapter();
            NoteChapter(chapter);
        }
    }

    private void GoNext()
    {
        if (_nextItem is { } next) SwitchTo(next);
        else if (_chapters)
        {
            var chapter = Player.Chapter + 1;
            Player.NextChapter();
            NoteChapter(chapter);
        }
    }

    private void NoteChapter(int chapter)
    {
        chapter = Math.Clamp(chapter, 0, Math.Max(0, Player.ChapterCount - 1));
        var name = _chapterList?.ElementAtOrDefault(chapter).Name;
        Feedback(string.IsNullOrWhiteSpace(name) ? $"Chapter {chapter + 1}" : $"Chapter {chapter + 1}: {name}");
    }

    /// <summary>The end of an item, or an error. Nothing ever shown: VLC couldn't play it.</summary>
    private void OnEnded()
    {
        if (_closing || _released) return;
        PlayerLog.Write($"ended at {PlayerControls.Format(Position)} of {PlayerControls.Format(Length)}");
        FailedToStart = _media.Statistics.DisplayedPictures == 0 && Position <= 0;
        // The next one only after a real end: a drive unplugged mid-film also ends it, early
        var reachedEnd = Length > 0 && Position >= Length * 0.9;
        if (reachedEnd && _nextItem is { } next)
        {
            EndItem();
            _offered = next;
            _secondsLeft = UpNextSeconds;
            _controls.ShowRow("message", UpNextText(), [("Play now", true, PlayNext), ("Cancel", false, () => _ = CloseAsync())]);
            _countdown.Start();
            ShowBar();
            return;
        }
        _ = CloseAsync();
    }

    private string UpNextText() => $"Up next in {_secondsLeft} s: {_offered?.Label}";

    private void CountDown()
    {
        if (--_secondsLeft > 0) { _controls.SetRowLabel(UpNextText()); return; }
        PlayNext();
    }

    private void PlayNext()
    {
        if (_offered is { } next) SwitchTo(next);
    }

    /// <summary>Another item in the same window (full screen stays): the next or previous episode.</summary>
    private void SwitchTo(NextUp next)
    {
        _countdown.Stop();
        _offered = null;
        if (_closing) return;
        _controls.HideRow();
        CloseMenu();
        EndItem();
        PlayerLog.Write($"switch to {next.Path}");
        _shownPictures = _lostPictures = 0;
        _pauseWait.Stop();
        _pause?.Close();
        _pause = null;
        _chapterList = null;
        _controls.SetChapters([]);
        _choice = next.Choice;
        _choicePending = true;
        _title = next.Title;
        _path = next.Path;
        var old = _media;
        _media = NewMedia(next.Path, next.StartMs, next.Choice);
        Tag = next.Tag;
        _itemEnded = false;
        FailedToStart = false;
        Interlocked.Exchange(ref _time, 0);
        Interlocked.Exchange(ref _length, 0);
        _window.SetTitle(next.Title);
        Player.Play(_media);
        old.Dispose();
        ItemStarted?.Invoke(Tag);
        UpdateSkip();
        ShowBar();
        if (next.StartMs > 0) ShowResumed(next.StartMs);
    }

    /// <summary>Reports the item playing now as stopped, once.</summary>
    private void EndItem()
    {
        if (_itemEnded) return;
        _itemEnded = true;
        ItemEnded?.Invoke(Tag, Position, Length);
    }

    /// <summary>Opens the Audio or Subtitles menu; the same button again closes it.</summary>
    private void ShowTracks(bool audio)
    {
        var kind = audio ? "audio" : "subtitles";
        var same = _menu?.Kind == kind;
        CloseMenu();
        if (same) return;
        OpenMenu(kind, Section(audio));
        ShowBar();
    }

    /// <summary>A right-click on the video: what the bar doesn't have, at the mouse.</summary>
    private void ShowContextMenu(int x, int y)
    {
        CloseMenu();
        List<MenuChoices> rows =
        [
            new("speed", "Speed", Speeds.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToList(), _speed),
            new("zoom", "Zoom", Zooms, _zoom),
        ];
        if (Player.Spu != -1) rows.Add(new("timing", "Subtitles", ["Earlier", "Later", "Reset"], -1));
        var menu = _menu = new PlayerMenu(_window.Hwnd, rows);
        menu.Chosen += ChooseMore;
        var r = _window.ClientOnScreen();
        menu.ShowAtMouse(r.Left + x, r.Top + y, r, _window.Scale);
    }

    private void ChooseMore(string kind, int place)
    {
        PlayerLog.Write($"{kind} {place} chosen");
        switch (kind)
        {
            case "speed":
                _speed = place;
                Player.SetRate(Speeds[_speed]);
                ShowNote(_speed == NormalSpeed ? "Normal speed" : $"Speed {Speeds[_speed].ToString(CultureInfo.InvariantCulture)}x");
                break;
            case "zoom":
                _zoom = place;
                ApplyZoom();
                ShowNote($"Zoom: {Zooms[_zoom]}");
                break;
            case "timing":
                MoveSubtitles(place switch { 0 => -SubtitleStepMs, 1 => SubtitleStepMs, _ => -(_choice?.SubtitleDelayMs ?? 0) });
                break;
        }
    }

    /// <summary>Fit shows the whole picture, Fill crops it to the shape of the window, the others set the picture's shape.</summary>
    private void ApplyZoom()
    {
        var r = _window.ClientOnScreen();
        Player.CropGeometry = _zoom == FillZoom ? $"{r.Right - r.Left}:{r.Bottom - r.Top}" : null;
        Player.AspectRatio = _zoom > FillZoom ? Zooms[_zoom] : null;
    }

    private MenuSection Section(bool audio)
    {
        var tracks = ((audio ? Player.AudioTrackDescription : Player.SpuDescription) ?? []).Select(t => (t.Id, t.Name ?? "")).ToList();
        if (!audio && SubtitleSearch.Available && UiSettings.FindSubtitlesOnline) tracks.Add((PlayerMenu.FindOnline, "Find subtitles online"));
        return new MenuSection(audio ? "audio" : "subtitles", audio ? "Audio" : "Subtitles", tracks, audio ? Player.AudioTrack : Player.Spu);
    }

    private void OpenMenu(string kind, MenuSection section)
    {
        CloseMenu();
        PlayerLog.Write($"{kind} menu: current {section.Current}: {string.Join(" | ", section.Tracks.Select(t => $"{t.Id} {t.Name}"))}");
        _menu = new PlayerMenu(_window.Hwnd, kind, section);
        _menu.Chosen += ChooseTrack;
    }

    /// <summary>A track chosen in a menu: used now, and kept for the next time.</summary>
    private void ChooseTrack(string kind, int id)
    {
        if (kind == "online") { AddSubtitles(_foundSubtitles[id]); return; }
        if (id == PlayerMenu.FindOnline) { FindSubtitles(); return; }
        var audio = kind == "audio";
        var tracks = (audio ? Player.AudioTrackDescription : Player.SpuDescription) ?? [];
        var done = audio ? Player.SetAudioTrack(id) : Player.SetSpu(id);
        PlayerLog.Write($"{kind} {id} chosen{(done ? "" : ", VLC refused")}");
        CloseMenu();
        _window.TakeFocus();

        var language = PlayerMenu.Language(tracks.FirstOrDefault(t => t.Id == id).Name);
        ShowNote(id == -1 ? (audio ? "Audio off" : "Subtitles off") : $"{(audio ? "Audio" : "Subtitles")}: {language}");
        var index = tracks.Where(t => t.Id != -1 && PlayerMenu.Language(t.Name) == language).TakeWhile(t => t.Id != id).Count();
        var choice = _choice ?? new TrackChoice(null, 0, null, 0, false, 0);
        if (!audio) KeepChoice(id == -1 ? choice with { Subtitles = null, SubtitlesOff = true } : choice with { Subtitles = language, SubtitleIndex = index, SubtitlesOff = false });
        else if (id != -1) KeepChoice(choice with { Audio = language, AudioIndex = index });
    }

    /// <summary>Looks online for subtitles for what's playing, and lists what it finds.</summary>
    private async void FindSubtitles()
    {
        CloseMenu();
        _window.TakeFocus();
        if (_findingSubtitles) return;
        _findingSubtitles = true;
        var tag = Tag;
        var film = FindSubtitleFilm?.Invoke(tag) ?? new SubtitleFilm(_title, null, null, null, null);
        PlayerLog.Write($"looking online for {film}");
        ShowNote("Looking for subtitles...", stay: true);
        var found = await SubtitleSearch.FindAsync(film, _path);
        _findingSubtitles = false;
        if (_closing || tag != Tag) return;
        PlayerLog.Write($"found {found.Hits.Count} subtitles online");
        if (found.Hits.Count == 0)
        {
            ShowNote(found.Error ?? "No subtitles found");
            return;
        }
        _noteHide.Stop();
        _note?.HideText();
        _foundSubtitles = found.Hits;
        var rows = found.Hits.Select((hit, i) => (i, $"{hit.FileName} - [{hit.Language}{(hit.ExactMatch ? " · made for this file" : "")}]")).ToList();
        OpenMenu("online", new MenuSection("online", "From OpenSubtitles.com", rows, -1));
        ShowBar();
    }

    /// <summary>Downloads subtitles found online and shows them.</summary>
    private async void AddSubtitles(SubtitleHit hit)
    {
        CloseMenu();
        _window.TakeFocus();
        var tag = Tag;
        ShowNote("Downloading subtitles...", stay: true);
        var saved = await SubtitleSearch.DownloadAsync(hit, _path);
        if (_closing || tag != Tag) return;
        PlayerLog.Write($"subtitles saved as {saved.Path}");
        var index = (Player.SpuDescription ?? []).Count(t => t.Id != -1 && PlayerMenu.Language(t.Name) == hit.Language);
        if (saved.Path is null || !Player.AddSlave(MediaSlaveType.Subtitle, new Uri(saved.Path).AbsoluteUri, true))
        {
            ShowNote(saved.Error ?? "Couldn't get those subtitles");
            return;
        }
        ShowNote($"Subtitles: {hit.Language}");
        KeepChoice((_choice ?? new TrackChoice(null, 0, null, 0, false, 0)) with { Subtitles = hit.Language, SubtitleIndex = index, SubtitlesOff = false });
    }

    private void KeepChoice(TrackChoice choice)
    {
        _choice = choice;
        ChoiceChanged?.Invoke(Tag, choice);
    }

    /// <summary>Moves the subtitles earlier (a negative step) or later, when they're out of sync.</summary>
    private void MoveSubtitles(int stepMs)
    {
        if (Player.Spu == -1) { ShowNote("No subtitles on"); return; }
        var choice = _choice ?? new TrackChoice(null, 0, null, 0, false, 0);
        var delay = choice.SubtitleDelayMs + stepMs;
        Player.SetSpuDelay(delay * 1000L);
        KeepChoice(choice with { SubtitleDelayMs = delay });
        var seconds = (Math.Abs(delay) / 1000.0).ToString("0.0", CultureInfo.CurrentCulture);
        ShowNote(delay == 0 ? "Subtitles on time" : $"Subtitles {seconds} s {(delay < 0 ? "earlier" : "later")}");
    }

    /// <summary>The menu's right edge under the Subtitles button, its bottom just above the controls.</summary>
    private void PlaceMenu()
    {
        if (_menu is null or { Kind: "more" }) return;
        var r = _window.ClientOnScreen();
        var scale = _window.Scale;
        _menu.ShowAt(r.Right - (int)Math.Round(PlayerControls.MenuAnchorFromRight * scale), _controls.Top - (int)Math.Round(8 * scale), scale);
    }

    private void CloseMenu()
    {
        // Forgotten first: closing it gives the player window back its activation, and the
        // move that brings would otherwise place the menu being closed
        var menu = _menu;
        _menu = null;
        if (menu is null) return;
        menu.Close();
        PlayerLog.Write($"{menu.Kind} menu closed");
    }

    /// <summary>
    /// A move shows the cursor. The controls come with it only near the bottom, the same in a
    /// window and in full screen, so moving the mouse elsewhere doesn't cover the film.
    /// </summary>
    private void OnMouseMoved(int x, int y)
    {
        var height = _window.ClientHeight;
        if (y >= height - Math.Max(160 * _window.Scale, height * 0.2))
        {
            ShowBar();
            return;
        }
        _window.SetCursorHidden(false);
        _cursor.Stop();
        _cursor.Start();
    }

    private void OnKey(int key, bool ctrl)
    {
        switch (key)
        {
            case 0x20: TogglePause(); break;                                          // Space
            case 0x1B when _menu != null: CloseMenu(); break;                         // Esc
            case 0x1B when _window.IsFullScreen: ToggleFullScreen(); break;
            case 0x1B: _ = CloseAsync(); break;
            case 0x0D when _offered != null: PlayNext(); break;                       // Enter
            case 0x4E: GoNext(); break;                                               // N
            case 0x50: GoPrevious(); break;                                           // P
            case 0x46 or PlayerWindow.VkF11: ToggleFullScreen(); break;               // F, F11, Alt+Enter
            case 0x25: SeekBy(ctrl ? -BigSeekStep : -SeekStep); break;                // Left
            case 0x27: SeekBy(ctrl ? BigSeekStep : SeekStep); break;                  // Right
            case 0x24: StartOver(); break;                                            // Home
            case 0x26: ChangeVolume(VolumeStep); break;                               // Up
            case 0x28: ChangeVolume(-VolumeStep); break;                              // Down
            case 0x4D: ToggleMute(); break;                                           // M
            case 0x47: MoveSubtitles(-SubtitleStepMs); break;                         // G
            case 0x48: MoveSubtitles(SubtitleStepMs); break;                          // H
        }
    }

    // A click pauses, unless a second one makes it a double-click
    private void OnClicked()
    {
        if (_menu != null) { CloseMenu(); return; }   // a click beside an open menu only closes it
        _click.Stop();
        _click.Start();
    }

    private void OnDoubleClicked()
    {
        _click.Stop();
        ToggleFullScreen();
    }

    private void OnWheel(int step) => ChangeVolume(step * VolumeStep);

    private void TogglePause()
    {
        var pausing = Player.IsPlaying;
        PlayerLog.Write(pausing ? "pause" : "play");
        Player.SetPause(pausing);
        _pauseWait.Stop();
        if (pausing) _pauseWait.Start();
        else _pause?.HideScreen();
        ShowBar();
    }

    /// <summary>The item's details, a few seconds into a pause.</summary>
    private void ShowPause()
    {
        if (_closing || Player.IsPlaying || _window.IsMinimized) return;
        if (_pause == null)
        {
            if (FindPauseInfo?.Invoke(Tag) is not { } info) return;
            _pause = new PlayerPause(_window.Hwnd, info);
            _pause.Clicked += OnClicked;
            _pause.DoubleClicked += OnDoubleClicked;
            _pause.RightClicked += ShowContextMenu;
            _pause.MouseMoved += OnMouseMoved;
            _pause.Wheel += OnWheel;
        }
        _pause.SetTimeLeft(Models.TimeLeft.Text(Player.Length - Player.Time));
        _pause.ShowOver(_window.ClientOnScreen(), _controls.Hwnd, _window.Scale);
        PlaceBar();
    }

    private void SeekBy(int ms)
    {
        PlayerLog.Write($"seek {ms / 1000:+0;-0} s");
        if (!Player.IsSeekable) return;
        var to = Math.Clamp(Player.Time + ms, 0, Math.Max(0, Player.Length - 1000));
        Player.Time = to;
        Feedback($"{(ms > 0 ? "+" : "-")}{Math.Abs(ms) / 1000} s  ({PlayerControls.Format(to)})");
    }

    /// <summary>What a key or the wheel did: the controls show it when they're up, else a small note.</summary>
    private void Feedback(string note)
    {
        if (_controls.Shown) ShowBar();
        else ShowNote(note);
    }

    private void ShowNote(string text, bool stay = false)
    {
        if (_closing) return;
        _note ??= PlayerLabel.Note(_window.Hwnd);
        var r = _window.ClientOnScreen();
        var scale = _window.Scale;
        var margin = (int)Math.Round(24 * scale);
        _note.ShowText(text, scale, (width, _) => new PointInt32(r.Right - width - margin, r.Top + margin));
        _noteHide.Stop();
        if (!stay) _noteHide.Start();
    }

    /// <summary>The time under the mouse on the seek bar, and its chapter.</summary>
    private void OnSeekHover(double? seconds, int screenX)
    {
        if (seconds is not { } at) { _tip?.HideText(); return; }
        _tip ??= PlayerLabel.Tip(_window.Hwnd);
        var name = _chapterList?.LastOrDefault(c => c.TimeOffset <= at * 1000).Name;
        var text = PlayerControls.Format((long)(at * 1000)) + (string.IsNullOrWhiteSpace(name) ? "" : "  " + name);
        var r = _window.ClientOnScreen();
        var top = _controls.Top;
        _tip.ShowText(text, _window.Scale, (width, height) => new PointInt32(Math.Clamp(screenX - width / 2, r.Left, Math.Max(r.Left, r.Right - width)), top - height));
    }

    private void SetVolume(int volume)
    {
        Volume = Math.Clamp(volume, 0, 100);
        Player.Volume = Volume;
    }

    private void ChangeVolume(int delta)
    {
        SetVolume(Volume + delta);
        Feedback($"Volume {Volume}%");
    }

    private void ToggleMute()
    {
        var muted = !Player.Mute;
        Player.Mute = muted;
        Feedback(muted ? "Muted" : "Sound on");
    }

    private void ToggleFullScreen()
    {
        _window.ToggleFullScreen();
        PlayerLog.Write(_window.IsFullScreen ? "full screen" : "window");
    }

    /// <summary>Shows the controls (and the cursor) for 3 s; they stay while paused, while
    /// the mouse is on them or while the row above the bar is open.</summary>
    private void ShowBar()
    {
        if (_closing) return;
        _window.SetCursorHidden(false);
        _barWanted = true;
        PlaceBar();
        Refresh();
        _tick.Start();
        _hide.Stop();
        _hide.Start();
    }

    private void HideBar()
    {
        if (_controls.PointerOver || _menu != null || _controls.Row.Length > 0 || !Player.IsPlaying)
        {
            _hide.Start();
            return;
        }
        _barWanted = false;
        _tick.Stop();
        PlaceBar();
        _window.SetCursorHidden(true);
    }

    /// <summary>Keeps the controls on the bottom of the video, and hidden when the player is.</summary>
    private void PlaceBar()
    {
        if (_closing) return;
        var visible = _window.IsVisible && !_window.IsMinimized;
        var shown = _barWanted && visible;
        var r = _window.ClientOnScreen();
        if (shown)
        {
            _controls.Place(r.Left, r.Bottom, r.Right - r.Left, _window.Scale);
            PlaceMenu();
        }
        else
        {
            _tip?.HideText();
            if (!visible) CloseMenu();
        }
        _controls.SetShown(shown);

        if (_pause is { Shown: true })
        {
            if (visible) _pause.ShowOver(r, _controls.Hwnd, _window.Scale);
            else _pause.HideScreen();
        }
        // Full screen has no title bar; the pause screen shows the title itself
        if (shown && _window.IsFullScreen && _pause is not { Shown: true })
        {
            _titleStrip ??= PlayerLabel.TitleStrip(_window.Hwnd);
            _titleStrip.ShowText(_title, _window.Scale, (_, _) => new PointInt32(r.Left, r.Top), r.Right - r.Left);
        }
        else _titleStrip?.HideText();
    }

    private void Refresh()
    {
        if (_released) return;
        // A film's chapters are known once it plays
        if (!_chapters && _previousItem == null && _nextItem == null && Player.ChapterCount > 1)
        {
            _chapters = true;
            ApplySkip();
        }
        _controls.Update(Player.Time, Player.Length, Player.IsPlaying, Player.Mute, Volume, _window.IsFullScreen);
        if (_chapterList == null && Player.ChapterCount > 1 && Player.Length > 0)
        {
            _chapterList = Player.FullChapterDescriptions();
            _controls.SetChapters(_chapterList.Select(c => c.TimeOffset / 1000.0).ToList());
        }
        if (_pause is { Shown: true }) _pause.SetTimeLeft(Models.TimeLeft.Text(Player.Length - Player.Time));
    }

    /// <summary>For the player log, every 2 s: is the picture still moving, and is VLC's window still there.</summary>
    private void LogHealth()
    {
        if (_released) return;
        var stats = _media.Statistics;
        PlayerLog.Write($"{PlayerControls.Format(Position)} {Player.State}, pictures +{stats.DisplayedPictures - _shownPictures}, lost +{stats.LostPictures - _lostPictures}"
                        + $" | {(_window.IsFullScreen ? "full screen" : "window")}, {_window.VlcWindowsState()} | focus {_window.Foreground()}"
                        + $"{(_menu is { } m ? $" | {m.Kind} menu open" : "")}{(_controls.Shown ? " | controls shown" : "")}{(_controls.PointerOver ? ", mouse on them" : "")}");
        _shownPictures = stats.DisplayedPictures;
        _lostPictures = stats.LostPictures;
    }

    private DispatcherQueueTimer Timer(int ms, Action tick, bool repeating = false)
    {
        var timer = _ui.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ms);
        timer.IsRepeating = repeating;
        timer.Tick += (_, _) => _ui.Post(tick);
        return timer;
    }

    /// <summary>Stops playback (off the UI thread: a slow drive can make it wait), then removes the windows.</summary>
    public async Task CloseAsync()
    {
        if (_closing) return;
        _closing = true;
        foreach (var t in new[] { _hide, _tick, _click, _countdown, _noteTimer, _cursor, _health, _noteHide, _pauseWait }) t.Stop();
        PlayerLog.Write($"close at {PlayerControls.Format(Position)}");
        EndItem();
        _offered = null;
        CloseMenu();
        _note?.Close();
        _tip?.Close();
        _titleStrip?.Close();
        _pause?.Close();
        _controls.Close();
        // Gone for the user at once: on a stalled drive VLC's stop waits for the read to return
        _window.Hide();
        await Task.Run(() => Player.Stop());
        // The player goes before its window: VLC must never draw into a destroyed window
        _released = true;
        Player.Dispose();
        _media.Dispose();
        _window.Destroy();
        Closed?.Invoke();
    }

    /// <summary>Empty once closed: the media is released then.</summary>
    public MediaStats Stats => _released ? default : _media.Statistics;
}
