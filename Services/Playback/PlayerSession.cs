using System.Globalization;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using MediaButton = Windows.Media.SystemMediaTransportControlsButton;

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
    private const int WheelStep = 10_000, BigSeekStep = 60_000, VolumeStep = 5, UpNextSeconds = 10;
    private const int SubtitleStepMs = 100;
    private const int NormalSpeed = 2, FitZoom = 0, FillZoom = 1;
    private static readonly float[] Speeds = [0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f];
    private static readonly string[] Zooms = ["Fit", "Fill", "16:9", "4:3"];
    // 5.4.3: Clear voices, on VLC's ten bands (31 Hz to 16 kHz): the rumble of explosions and music down, the
    // range of speech (1 to 4 kHz) up. An equalizer, not a compressor: loud and quiet scenes stay as they are
    private static readonly float[] ClearVoicesBands = [-10, -8, -5, -2, 0, 2, 4, 3, 1, -1];
    private const float ClearVoicesPreamp = 2;
    // 5.4.3: after a pause this long, playing on starts from a clean picture
    private static readonly TimeSpan LongPause = TimeSpan.FromSeconds(10);
    // 5.4.3: the waits before opening again a file that stopped answering (s): about 40 s in all, what a
    // drive or a network share takes to wake up
    private static readonly int[] RecoverDelays = [1, 3, 6, 12, 20];

    private readonly LibVLC _vlc;
    private readonly PlayerWindow _window;
    private readonly PlayerControls _controls;
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _hide, _tick, _click, _countdown, _noteTimer, _cursor, _health, _noteHide, _pauseWait, _credits;
    // 5.3.0: where this item's credits start (null: not known), whether that was worked out, the offer shown,
    // and whether it has passed the point where it counts as watched
    private long? _creditsMs;
    private bool _creditsChecked, _creditsOffered, _watchedReached;
    // 5.3.0: an intro or a recap to skip (ms), the one last offered, a scene after the credits, and the
    // times asked for outside the file while they come
    private List<(long Start, long End, string Label)> _skips = [];
    private long _skipOffered = -1;
    private long? _afterCredits;
    private Task<SkipTimes?>? _timesTask;
    // 5.3.0: several jumps forward in a row (+10 s pressed a few times) count as one
    private long _jumpFrom = -1, _jumpTo;
    private DateTime _jumpAt;
    private Media _media;
    private long _time, _length;
    // 5.4.3: how far the skip buttons and the arrows jump (ms), as set in Settings when the player opened
    private readonly int _backMs, _forwardMs;
    // 5.4.3: where the video really is, without a jump VLC makes when a read fails
    private readonly PlaybackClock _clock = new();
    // 5.4.3: opening the file again after it stopped answering
    private bool _recovering;
    private int _recoveries, _sameEnds;
    private long _recoveredAt;
    private Task? _stopping;
    private readonly System.Diagnostics.Stopwatch _pausedFor = new();
    // 5.4.3: Windows' media keys and media panel; when a key last came to the player window itself
    private MediaKeys? _keys;
    private long _mediaKeyAt;
    private bool _mediaInfoPending = true;
    private bool _barWanted, _closing, _released, _itemEnded;
    private NextUp? _offered, _previousItem, _nextItem;
    private Func<object?, NextUp?>? _findPrevious, _findNext;
    private PlayerMenu? _menu;
    private PlayerLabel? _note, _tip, _titleStrip;
    private PlayerPause? _pause;
    private TrackChoice? _choice;
    private bool _choicePending = true;
    // 5.1.0 (#25): subtitle files beside the video, and which of VLC's tracks each became
    private IReadOnlyList<string> _subtitleFiles = [];
    private readonly Dictionary<int, string> _fileTracks = [];
    private readonly Queue<(string File, int[] Before)> _addingFiles = new();
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

    /// <summary>Where it is (ms), as VLC last said, leaving out a jump it makes when a read fails (5.4.3); safe to read after closing.</summary>
    public long Position => _clock.Position;
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
    /// <summary>5.3.0: once per item, when it plays: its tag, its file and the languages of the tracks inside it.</summary>
    public event Action<object?, string, (string? Audio, string? Subtitles)>? LanguagesKnown;
    /// <summary>5.3.0: the next item asked for before the end (Next, or the offer at the credits): the tag,
    /// the position and the length (ms).</summary>
    public event Action<object?, long, long>? SkippedToNext;
    /// <summary>5.3.0: where an item's intro, recap and credits are when its chapters don't say (TheIntroDB, or
    /// learned in the season): from its tag and length (ms). It may take a moment.</summary>
    public Func<object?, long, Task<SkipTimes?>>? FindSkipTimes { get; set; }
    /// <summary>5.3.0: the player was jumped forward near an item's start (one jump, or a few in a row): the
    /// tag, from and to (ms). An intro, most likely.</summary>
    public event Action<object?, long, long>? SkippedIntro;
    /// <summary>5.3.0: where the playing item's credits start (ms), once known; read when it ends.</summary>
    public long? CreditsMs => _creditsMs;
    /// <summary>5.3.0: once per item, as it passes 90% or its credits: marks it watched there and then, and
    /// gives the note to show then ("Marked as watched"), or null for none.</summary>
    public Func<object?, long, long, string?>? MarkWatched { get; set; }
    /// <summary>5.3.0: the playing item has passed that point (and <see cref="MarkWatched"/> was asked).</summary>
    public bool WatchedReached => _watchedReached;
    /// <summary>What the pause screen shows for an item; null shows none.</summary>
    public Func<object?, PauseInfo?>? FindPauseInfo { get; set; }
    /// <summary>What to look for subtitles online by; null goes by the title alone.</summary>
    public Func<object?, SubtitleFilm?>? FindSubtitleFilm { get; set; }
    /// <summary>5.4.3: what Windows' media panel shows under an item's title, and its poster file.</summary>
    public Func<object?, (string? Line, string? Poster)>? FindMediaInfo { get; set; }

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
        _backMs = UiSettings.SkipBackSeconds * 1000;
        _forwardMs = UiSettings.SkipForwardSeconds * 1000;
        SetGood(startMs);
        Player = new MediaPlayer(vlc) { EnableMouseInput = false, EnableKeyInput = false, Hwnd = window.Hwnd };
        if (UiSettings.ClearVoices) ApplyClearVoices();
        _media = NewMedia(path, startMs, choice);
        _controls = new PlayerControls(window.Hwnd, UiSettings.SkipBackSeconds, UiSettings.SkipForwardSeconds);
        _controls.SetFile(path);
        _keys = MediaKeys.TryCreate(window.Hwnd);
        if (_keys != null) _keys.Pressed += button => _ui.Post(() => OnMediaKey(button));

        _hide = Timer(3000, HideBar);
        _tick = Timer(250, Refresh, repeating: true);
        _click = Timer(PlayerWindow.DoubleClickTime, TogglePause);
        _countdown = Timer(1000, CountDown, repeating: true);
        _noteTimer = Timer(8000, () => { if (_controls.Row == "message" && _offered == null) _controls.HideRow(); });
        _cursor = Timer(3000, () => { if (!_barWanted && Player.IsPlaying) _window.SetCursorHidden(true); });
        _health = Timer(2000, LogHealth, repeating: true);
        _noteHide = Timer(1500, () => _note?.HideText());
        _pauseWait = Timer(4000, ShowPause);
        _credits = Timer(1000, WatchEnd, repeating: true);

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
        _controls.Back += () => SeekBy(-_backMs);
        _controls.Forward += () => SeekBy(_forwardMs);
        _controls.Mute += ToggleMute;
        _controls.FullScreen += ToggleFullScreen;
        _controls.CloseClicked += () => _ = CloseAsync();
        _controls.Audio += () => ShowTracks(audio: true);
        _controls.Subtitles += () => ShowTracks(audio: false);
        _controls.Previous += GoPrevious;
        _controls.Next += GoNext;
        _controls.Seek += seconds =>
        {
            var (from, to) = (Player.Time, (long)(seconds * 1000));
            JumpTo(to);
            NoteJump(from, to);   // 5.3.0: where it was asked to go (VLC's time catches up a moment later)
        };
        _controls.Volume += SetVolume;
        _controls.SeekWheel += notches => SeekBy(notches * WheelStep);
        _controls.VolumeWheel += notches => ChangeVolume(notches * VolumeStep);
        _controls.SeekHover += OnSeekHover;
        _controls.Released += window.TakeFocus;
        _controls.Moved += PlaceBar;

        // VLC raises its events on its own threads, and must not be called back from them
        Player.TimeChanged += (_, e) =>
        {
            Interlocked.Exchange(ref _time, e.Time);
            _clock.Track(e.Time, Environment.TickCount64);
        };
        Player.LengthChanged += (_, e) => Interlocked.Exchange(ref _length, e.Length);
        Player.Playing += (_, _) => _ui.Post(() =>
        {
            if (_released) return;
            Player.Volume = Volume;
            if (_speed != NormalSpeed) Player.SetRate(Speeds[_speed]);
            if (_zoom != FitZoom) ApplyZoom();
            if (_choicePending) ApplyChoice();
            _keys?.SetPlaying(true);
            if (_mediaInfoPending) ShowMediaInfo();
        });
        Player.Paused += (_, _) => _ui.Post(() => { if (!_released) _keys?.SetPlaying(false); });
        Player.Stopped += (_, _) => _ui.Post(() => { if (!_released) _keys?.SetPlaying(false); });
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
        // Once shown: going back to a window restores the place it had then
        if (UiSettings.PlayerFullScreen) session.ToggleFullScreen();
        beforePlay?.Invoke(session.Player);
        session.Player.Play(session._media);
        session._health.Start();
        session._credits.Start();
        session.ShowBar();
        if (startMs > 0) session.ShowResumed(startMs);
        return (session, null);
    }

    // A path, not a URI: VLC makes the URI itself, so '#', '%' and any script in names are safe
    private Media NewMedia(string path, long startMs, TrackChoice? choice)
    {
        var media = new Media(_vlc, path, FromType.FromPath);
        media.AddOption(":sub-autodetect-path=.\\none");   // VLC ignores an empty one; those folders are listed by SubtitleFiles, by name
        if (startMs > 0) media.AddOption(":start-time=" + (startMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
        // From the first sound, not a moment after the film started in another language
        if (choice?.Audio is { } audio) media.AddOption(":audio-language=" + audio);
        return media;
    }

    /// <summary>Once the tracks are known: the chosen ones, when the file has them.</summary>
    private void ApplyChoice()
    {
        _choicePending = false;
        // What's inside the file, before a subtitle file is added
        try { if (_media.Tracks is { Length: > 0 } tracks) LanguagesKnown?.Invoke(Tag, _path, FileLanguages.FromTracks(tracks)); }
        catch (Exception e) when (e is VLCException or InvalidOperationException or ObjectDisposedException) { PlayerLog.Write("tracks: " + e.Message); }
        _subtitleFiles = SubtitleFiles.Near(_path);
        _fileTracks.Clear();
        _addingFiles.Clear();
        Player.SetSpuDelay((_choice?.SubtitleDelayMs ?? 0) * 1000L);
        var c = _choice;
        if (c?.Audio is { } audio && Pick(Player.AudioTrackDescription, audio, c.AudioIndex) is { } a && a != Player.AudioTrack)
            Player.SetAudioTrack(a);
        if (c?.SubtitlesOff == true)
        {
            if (Player.Spu != -1) Player.SetSpu(-1);
        }
        else if (c?.Subtitles is { } subtitles)
        {
            if (FileFor(subtitles, c.SubtitleIndex) is { } file) OpenSubtitleFile(file);
            else if (Pick(Player.SpuDescription, subtitles, c.SubtitleIndex) is { } s && s != Player.Spu) Player.SetSpu(s);
        }
        else if (SubtitleFiles.NamedAfter(_subtitleFiles, _path) is { } own) OpenSubtitleFile(own);
        PlayerLog.Write($"tracks: audio {Player.AudioTrack}, subtitles {Player.Spu}, {_subtitleFiles.Count} files, wanted {c}");
    }

    private static bool SameLanguage(string? name, string language) =>
        string.Equals(PlayerMenu.Language(name), language, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file a kept choice means, when it's past the tracks inside the video of that language.</summary>
    private string? FileFor(string language, int index)
    {
        var inside = (Player.SpuDescription ?? []).Count(t => t.Id != -1 && SameLanguage(t.Name, language));
        var files = _subtitleFiles.Where(f => SameLanguage(SubtitleFiles.Label(f), language)).ToList();
        return index >= inside && files.Count > 0 ? files[Math.Min(index - inside, files.Count - 1)] : null;
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
        JumpTo(0);
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
        _keys?.SetNextPrevious(_nextItem != null || _chapters, _previousItem != null || _chapters);
    }

    private void GoPrevious()
    {
        if (_previousItem is { } previous) SwitchTo(previous);
        else if (_chapters)
        {
            var chapter = Player.Chapter - 1;
            Asked();
            Player.PreviousChapter();
            NoteChapter(chapter);
        }
    }

    private void GoNext()
    {
        if (_nextItem is { } next) SkipTo(next);
        else if (_chapters)
        {
            var chapter = Player.Chapter + 1;
            Asked();
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
        if (_closing || _released || _recovering) return;
        var (position, length) = (Position, Length);
        PlayerLog.Write($"ended at {PlayerControls.Format(position)} of {PlayerControls.Format(length)} (VLC's clock {PlayerControls.Format(Interlocked.Read(ref _time))})");
        FailedToStart = _media.Statistics.DisplayedPictures == 0 && Interlocked.Read(ref _time) <= 0;
        // The next one only after a real end: a drive unplugged mid-film also ends it, early
        var reachedEnd = length > 0 && position >= length * 0.9;
        // 5.4.3: well before its end, after it played: the file stopped answering (its drive unplugged or asleep,
        // the computer back from sleep), so it's opened again where it was. Stopping at the same place again
        // after that says the file is shorter than it claims
        if (!reachedEnd && length > 0 && !FailedToStart)
        {
            if (_recoveries > 0 && Math.Abs(position - _recoveredAt) <= 3000 && ++_sameEnds >= 2) ShowStopped(endsHere: true);
            else _ = RecoverAsync();
            return;
        }
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

    /// <summary>
    /// 5.4.3: the file stopped answering before its end. It's stopped, then opened again where it was as soon
    /// as the file is there, waiting longer each time, about 40 s in all. Nothing is marked watched for it.
    /// </summary>
    private async Task RecoverAsync()
    {
        if (_recovering || _closing) return;
        _recovering = true;
        var (tag, path, at) = (Tag, _path, Position);
        _countdown.Stop();
        PlayerLog.Write($"the file stopped answering at {PlayerControls.Format(at)}: opening it again");
        ShowStoppedRow("The file stopped answering. Opening it again...", [("Close player", false, () => _ = CloseAsync())]);
        _stopping = Task.Run(() => Player.Stop());   // it waits while a read is stuck
        await _stopping;
        for (; _recoveries < RecoverDelays.Length; _recoveries++)
        {
            if (Gone()) return;
            await Task.Delay(TimeSpan.FromSeconds(RecoverDelays[_recoveries]));
            if (Gone()) return;
            if (!await AnswersAsync(path)) continue;
            if (Gone()) return;
            _recoveries++;
            _recoveredAt = at;
            _recovering = false;
            Reopen(at);
            return;
        }
        if (!Gone()) ShowStopped(endsHere: false);

        // closed, or another episode asked for meanwhile
        bool Gone() => _closing || _released || !_recovering || tag != Tag;
    }

    private static async Task<bool> AnswersAsync(string path)
    {
        try { return await Task.Run(() => File.Exists(path)).WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { return false; }
    }

    /// <summary>5.4.3: the same file again from where it was, with the tracks chosen. VLC's clock from before is
    /// kept, so a file that still won't play isn't taken for one that never could (and handed to another player).</summary>
    private void Reopen(long at)
    {
        PlayerLog.Write($"opening it again at {PlayerControls.Format(at)}");
        _controls.HideRow();
        var old = _media;
        _media = NewMedia(_path, at, _choice);
        _choicePending = true;
        _shownPictures = _lostPictures = 0;
        SetGood(at);
        Player.Play(_media);
        old.Dispose();
    }

    /// <summary>5.4.3: the file still doesn't answer, or stops at the same place again: the user says what next.
    /// Where it was is kept.</summary>
    private void ShowStopped(bool endsHere)
    {
        _recovering = false;
        PlayerLog.Write(endsHere ? "stopped at the same place again" : "the file still doesn't answer");
        var buttons = new List<(string, bool, Action)> { ("Try again", true, TryAgain) };
        if (_nextItem is { } next) buttons.Add(("Play next episode", false, () => SkipTo(next)));
        buttons.Add(("Close player", false, () => _ = CloseAsync()));
        ShowStoppedRow(endsHere ? "The video stops here, before its end" : "The file doesn't answer. Is its drive asleep or unplugged?", buttons);
    }

    private void TryAgain()
    {
        _recoveries = _sameEnds = 0;
        _ = RecoverAsync();
    }

    private void ShowStoppedRow(string text, List<(string, bool, Action)> buttons)
    {
        _noteTimer.Stop();
        _controls.ShowRow("stopped", text, buttons);
        ShowBar();
    }

    /// <summary>5.4.3: a jump to a place VLC works out (a chapter).</summary>
    private void Asked() => _clock.Asked(Environment.TickCount64);

    /// <summary>5.4.3: a jump asked for (a seek, a skip).</summary>
    private void JumpTo(long ms)
    {
        _clock.Jump(ms, Environment.TickCount64);
        Player.Time = ms;
    }

    /// <summary>5.4.3: where a video starts.</summary>
    private void SetGood(long ms) => _clock.Start(ms, Environment.TickCount64);

    private void CountDown()
    {
        if (--_secondsLeft > 0) { _controls.SetRowLabel(UpNextText()); return; }
        PlayNext();
    }

    private void PlayNext()
    {
        if (_offered is { } next) SwitchTo(next);
    }

    /// <summary>5.3.0: the next item asked for before this one ended; where that was tells where the credits start.</summary>
    private void SkipTo(NextUp next)
    {
        SkippedToNext?.Invoke(Tag, Position, Length);
        SwitchTo(next);
    }

    /// <summary>
    /// 5.3.0: once a second while playing. Past 90%, or past the credits when they start sooner, the item
    /// is marked watched there and then, with a note. At the credits the next episode is offered, or for
    /// a film (or a show's last episode here) a way to close. Nothing starts or closes by itself there: a
    /// film may have a scene after them, and the next episode still waits for the end.
    /// </summary>
    private void WatchEnd()
    {
        if (_released || _closing || !Player.IsPlaying) return;
        try { WatchEndOnce(); }
        catch (Exception e) { PlayerLog.Write("watching the end: " + e.Message); }   // never out of a timer's tick
    }

    private void WatchEndOnce()
    {
        var length = Length;
        var position = Position;
        if (length <= 0) return;
        if (!_creditsChecked)
        {
            _creditsChecked = true;
            ChapterDescription[] chapters = Player.ChapterCount > 1 ? Player.FullChapterDescriptions() : [];
            var named = chapters.Select(c => (c.TimeOffset, c.Name)).ToList();
            _creditsMs = CreditsChapter(named, length);
            _skips = SkipChapters(named, length);
            if (_creditsMs is long found) PlayerLog.Write($"credits from {PlayerControls.Format(found)} (chapter)");
            if (_creditsMs is null || _skips.Count == 0)
                _timesTask = FindSkipTimes?.Invoke(Tag, length);   // for what the chapters don't say
        }
        if (_timesTask is { IsCompleted: true } asked)
        {
            _timesTask = null;
            if (asked.IsCompletedSuccessfully && asked.Result is { } times) UseTimes(times, length);
        }
        if (_jumpFrom >= 0 && DateTime.UtcNow - _jumpAt > TimeSpan.FromSeconds(4)) CloseJump();
        OfferSkip(position, again: false);
        if (!_watchedReached && (position >= length * 0.9 || _creditsMs is long credits && credits > length / 2 && position >= credits))
        {
            _watchedReached = true;
            PlayerLog.Write($"watched at {PlayerControls.Format(position)}");
            if (MarkWatched?.Invoke(Tag, position, length) is { } note) ShowNote(note, ms: 3000);
        }
        if (_creditsMs is not long at) return;
        if (position < at - 5000) { _creditsOffered = false; return; }   // gone back before them: offered again
        if (_creditsOffered || position < at || position > length - 3000) return;
        _creditsOffered = true;
        PlayerLog.Write($"at the credits, {PlayerControls.Format(position)}");
        if (_menu != null || _controls.Row.Length > 0 && _controls.Row != "message") return;   // never over something open
        var buttons = new List<(string, bool, Action)>();
        if (_nextItem is { } next) buttons.Add(("Play next episode", true, () => SkipTo(next)));
        if (_afterCredits is long scene) buttons.Add(("Skip to the scene after", buttons.Count == 0, () => { JumpTo(scene); _controls.HideRow(); }));
        if (_nextItem is null) buttons.Add(("Close player", buttons.Count == 0, () => _ = CloseAsync()));
        _controls.ShowRow("message", _nextItem is { } n ? $"Next: {n.Label}" : _afterCredits is null ? "Credits" : "Credits · a scene comes after them", buttons);
        _noteTimer.Stop();
        _noteTimer.Start();   // gone after a few seconds, the bar after it
        ShowBar();
    }

    /// <summary>5.3.0: times from outside the file (TheIntroDB, or learned in the season), for what its chapters don't say.</summary>
    private void UseTimes(SkipTimes t, long length)
    {
        if (_creditsMs is null && t.Credits is long c && c > length / 2 && c < length) _creditsMs = c;
        if (t.AfterCredits is long a && _creditsMs is long credits && a > credits && a < length - 2000) _afterCredits = a;
        if (_skips.Count == 0)
        {
            if (Span(t.RecapStart, t.RecapEnd, length) is { } recap) _skips.Add((recap.Start, recap.End, "Skip recap"));
            if (Span(t.IntroStart, t.IntroEnd, length) is { } intro) _skips.Add((intro.Start, intro.End, "Skip intro"));
        }
        PlayerLog.Write($"skip times: credits {_creditsMs}, scene after {_afterCredits}; {string.Join(", ", _skips.Select(s => $"{s.Label} {s.Start}-{s.End}"))}");
    }

    private static (long Start, long End)? Span(long? start, long? end, long length) =>
        start is long s && end is long e && s >= 0 && e - s >= 5000 && e <= length / 2 ? (s, e) : null;

    /// <summary>
    /// 5.3.0: inside an intro or a recap, a button to jump past it: shown on going in (with the bar, gone
    /// with it), and again when the mouse brings the bar back up inside it.
    /// </summary>
    private void OfferSkip(long position, bool again)
    {
        var inside = _skips.FirstOrDefault(s => position >= s.Start && position < s.End - 2000);
        if (inside.Label is null) return;
        if (again ? _controls.Row.Length > 0 : _skipOffered == inside.Start) return;
        if (_menu != null || _controls.Row.Length > 0 && _controls.Row != "message") return;
        var first = _skipOffered != inside.Start;
        _skipOffered = inside.Start;
        var end = inside.End;
        _controls.ShowRow("message", inside.Label == "Skip recap" ? "Recap" : "Intro",
            [(inside.Label, true, () => { JumpTo(end); _controls.HideRow(); PlayerLog.Write($"{inside.Label} to {PlayerControls.Format(end)}"); })]);
        _noteTimer.Stop();
        _noteTimer.Start();
        if (first) ShowBar();
    }

    /// <summary>
    /// 5.3.0: chapters named for an intro or a recap in the first part ("Intro", "Opening", "Recap",
    /// "Previously on"), from their start to the next chapter's.
    /// </summary>
    internal static List<(long Start, long End, string Label)> SkipChapters(IReadOnlyList<(long Offset, string? Name)> chapters, long length)
    {
        var spans = new List<(long, long, string)>();
        for (int i = 0; i + 1 < chapters.Count; i++)
        {
            var (start, name) = chapters[i];
            var end = chapters[i + 1].Offset;
            if (string.IsNullOrWhiteSpace(name) || start > length * 0.4 || end - start < 5000 || end - start > 5 * 60_000) continue;
            var n = name.Trim().ToLowerInvariant();
            if (n.Contains("recap") || n.StartsWith("previously")) spans.Add((start, end, "Skip recap"));
            else if (n.Contains("intro") || n.StartsWith("opening") || n is "op" or "title sequence" or "titles" or "main titles" or "theme")
                spans.Add((start, end, "Skip intro"));
        }
        return spans;
    }

    private void NoteJump(long from, long to)
    {
        var now = DateTime.UtcNow;
        if (_jumpFrom < 0 || now - _jumpAt > TimeSpan.FromSeconds(4)) { CloseJump(); _jumpFrom = from; }
        _jumpTo = to;
        _jumpAt = now;
    }

    /// <summary>5.3.0: one jump forward of 15 s to 3 min from the first 10 minutes: an intro, most likely.</summary>
    private void CloseJump()
    {
        if (_jumpFrom < 0) return;
        var (from, to) = (_jumpFrom, _jumpTo);
        _jumpFrom = -1;
        if (from < 10 * 60_000 && to - from >= 15_000 && to - from <= 180_000) SkippedIntro?.Invoke(Tag, from, to);
    }

    /// <summary>
    /// 5.3.0: where the credits start, from a chapter named so in the second half: "Credits", "End Credits",
    /// "Closing Credits", "Ending", "End Titles". Opening credits and chapters that don't say tell nothing.
    /// </summary>
    internal static long? CreditsChapter(IReadOnlyList<(long Offset, string? Name)> chapters, long length)
    {
        foreach (var (offset, name) in chapters)
        {
            if (offset < length / 2 || offset >= length || string.IsNullOrWhiteSpace(name)) continue;
            var n = name.Trim().ToLowerInvariant();
            if (n.Contains("credit") && !n.Contains("open") || n is "ending" or "end" or "ed" or "outro" or "end titles" or "closing")
                return offset;
        }
        return null;
    }

    /// <summary>Another item in the same window (full screen stays): the next or previous episode.</summary>
    private void SwitchTo(NextUp next)
    {
        if (_stopping is { IsCompleted: false }) return;   // 5.4.3: a file that stopped answering is still being stopped
        _countdown.Stop();
        _offered = null;
        if (_closing) return;
        _recovering = false;   // 5.4.3: the next one instead of opening this one again
        _recoveries = _sameEnds = 0;
        _pausedFor.Reset();
        _mediaInfoPending = true;
        _controls.HideRow();
        CloseMenu();
        EndItem();
        _creditsMs = null;   // 5.3.0: the next one's own
        _creditsChecked = _creditsOffered = _watchedReached = false;
        _skips = [];
        _skipOffered = _jumpFrom = -1;
        _afterCredits = null;
        _timesTask = null;
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
        _controls.SetFile(next.Path);
        var old = _media;
        _media = NewMedia(next.Path, next.StartMs, next.Choice);
        Tag = next.Tag;
        _itemEnded = false;
        FailedToStart = false;
        Interlocked.Exchange(ref _time, 0);
        Interlocked.Exchange(ref _length, 0);
        SetGood(next.StartMs);
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
            new("sound", "Sound", ["Normal", "Clear voices"], UiSettings.ClearVoices ? 1 : 0),   // 5.4.3
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
            case "sound":
                SetClearVoices(place == 1);
                break;
        }
    }

    /// <summary>5.4.3: V or the right-click menu: Clear voices on or off, kept for the next films.</summary>
    private void SetClearVoices(bool on)
    {
        UiSettings.SetClearVoices(on);
        ApplyClearVoices();
        ShowNote(on ? "Clear voices on" : "Clear voices off");
    }

    /// <summary>5.4.3: the equalizer stays with the player, for the next episodes too.</summary>
    private void ApplyClearVoices()
    {
        if (!UiSettings.ClearVoices)
        {
            Player.UnsetEqualizer();
            return;
        }
        using var equalizer = new Equalizer();
        equalizer.SetPreamp(ClearVoicesPreamp);
        for (uint band = 0; band < Math.Min(equalizer.BandCount, (uint)ClearVoicesBands.Length); band++)
            equalizer.SetAmp(ClearVoicesBands[band], band);
        Player.SetEqualizer(equalizer);
    }

    /// <summary>5.4.3: the next speed up or down, from the keys.</summary>
    private void ChangeSpeed(int step) => ChooseMore("speed", Math.Clamp(_speed + step, 0, Speeds.Length - 1));

    /// <summary>5.4.3: A and S: the next audio track, or the next subtitles (Off among them), kept as if chosen in the menu.</summary>
    private void CycleTrack(bool audio)
    {
        var tracks = Tracks(audio).Where(t => !audio || t.Id != -1).ToList();
        if (tracks.Count < 2)
        {
            ShowNote(audio ? "No other audio track" : "No subtitles in this file");
            return;
        }
        var current = tracks.FindIndex(t => t.Id == (audio ? Player.AudioTrack : Player.Spu));
        ChooseTrack(audio ? "audio" : "subtitles", tracks[(current + 1) % tracks.Count].Id);
    }

    /// <summary>5.4.3: C: the chapters in a menu, the one playing ticked; the same key again closes it.</summary>
    private void ShowChapters()
    {
        var same = _menu?.Kind == "chapters";
        CloseMenu();
        if (same) return;
        if (Player.ChapterCount < 2)
        {
            ShowNote("No chapters in this file");
            return;
        }
        var chapters = _chapterList ?? Player.FullChapterDescriptions();
        // As "time - [name]": the menu shows what's in the brackets first and the rest dimmed after it, as for a track
        var rows = chapters.Select((c, i) => (i, $"{PlayerControls.Format(c.TimeOffset)} - [{(string.IsNullOrWhiteSpace(c.Name) ? $"Chapter {i + 1}" : c.Name.Trim())}]")).ToList();
        OpenMenu("chapters", new MenuSection("chapters", "Chapters", rows, Player.Chapter));
        ShowBar();
    }

    private void GoToChapter(int chapter)
    {
        CloseMenu();
        _window.TakeFocus();
        Asked();
        Player.Chapter = chapter;
        NoteChapter(chapter);
    }

    /// <summary>5.4.3: I: past the intro or the recap playing now, as its button does.</summary>
    private void SkipNow()
    {
        var position = Position;
        var inside = _skips.FirstOrDefault(s => position >= s.Start && position < s.End - 2000);
        if (inside.Label is null)
        {
            ShowNote("Nothing to skip here");
            return;
        }
        JumpTo(inside.End);
        if (_controls.Row == "message") _controls.HideRow();
        PlayerLog.Write($"{inside.Label} to {PlayerControls.Format(inside.End)} (key)");
        Feedback(inside.Label == "Skip recap" ? "Recap skipped" : "Intro skipped");
    }

    /// <summary>5.4.3: a media key, a remote's or headphones' button, through Windows (also when another window is in front).</summary>
    private void OnMediaKey(MediaButton button)
    {
        if (_closing || _released) return;
        if (Environment.TickCount64 - _mediaKeyAt < 500) return;   // the same press, already taken by the player window
        PlayerLog.Write($"media key {button}");
        switch (button)
        {
            case MediaButton.Play when !Player.IsPlaying:
            case MediaButton.Pause when Player.IsPlaying:
                TogglePause();
                break;
            case MediaButton.Stop: _ = CloseAsync(); break;
            case MediaButton.FastForward: SeekBy(_forwardMs); break;
            case MediaButton.Rewind: SeekBy(-_backMs); break;
            case MediaButton.Next: GoNext(); break;
            case MediaButton.Previous: GoPrevious(); break;
        }
    }

    /// <summary>A media key came to the player window: Windows may pass on the same press.</summary>
    private void MediaKeyHere() => _mediaKeyAt = Environment.TickCount64;

    /// <summary>5.4.3: what Windows' media panel shows for the item playing now.</summary>
    private void ShowMediaInfo()
    {
        _mediaInfoPending = false;
        if (_keys is null) return;
        (string? Line, string? Poster) info = (null, null);
        try { info = FindMediaInfo?.Invoke(Tag) ?? (null, null); }
        catch (Exception e) { PlayerLog.Write("media panel: " + e.Message); }
        _keys.SetInfo(_title, info.Line, info.Poster);
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
        var tracks = Tracks(audio);
        if (!audio)
        {
            var open = _fileTracks.Values.Concat(_addingFiles.Select(a => a.File)).ToHashSet();
            tracks.AddRange(_subtitleFiles.Select((file, i) => (File: file, Row: (PlayerMenu.FirstFile - i, SubtitleFiles.Label(file))))
                .Where(f => !open.Contains(f.File)).Select(f => f.Row));
            tracks.Add((PlayerMenu.AddFile, "Add subtitle file..."));
        }
        if (!audio && SubtitleSearch.Available && UiSettings.FindSubtitlesOnline) tracks.Add((PlayerMenu.FindOnline, "Find subtitles online"));
        return new MenuSection(audio ? "audio" : "subtitles", audio ? "Audio" : "Subtitles", tracks, audio ? Player.AudioTrack : Player.Spu);
    }

    /// <summary>A menu's tracks, a subtitle file by its own name and not VLC's "Track 3".</summary>
    private List<(int Id, string Name)> Tracks(bool audio)
    {
        if (!audio) NoteAddedFiles();
        return ((audio ? Player.AudioTrackDescription : Player.SpuDescription) ?? [])
            .Select(t => (t.Id, !audio && _fileTracks.TryGetValue(t.Id, out var file) ? SubtitleFiles.Label(file) : t.Name ?? "")).ToList();
    }

    // VLC adds a file's track a moment later and doesn't say which it is
    private void NoteAddedFiles()
    {
        while (_addingFiles.TryPeek(out var adding))
        {
            var fresh = (Player.SpuDescription ?? []).Select(t => t.Id)
                .Where(id => id != -1 && !adding.Before.Contains(id) && !_fileTracks.ContainsKey(id)).ToList();
            if (fresh.Count == 0) return;
            _fileTracks[fresh[0]] = adding.File;
            _addingFiles.Dequeue();
        }
    }

    private bool OpenSubtitleFile(string file)
    {
        var before = (Player.SpuDescription ?? []).Select(t => t.Id).ToArray();
        if (!Player.AddSlave(MediaSlaveType.Subtitle, new Uri(file).AbsoluteUri, true)) return false;
        _addingFiles.Enqueue((file, before));
        return true;
    }

    /// <summary>A subtitle file chosen in the menu or the file window: shown now, and kept for the next time.</summary>
    private void UseSubtitleFile(string file)
    {
        CloseMenu();
        _window.TakeFocus();
        var language = PlayerMenu.Language(SubtitleFiles.Label(file));
        var index = (Player.SpuDescription ?? []).Count(t => t.Id != -1 && !_fileTracks.ContainsKey(t.Id) && SameLanguage(t.Name, language))
                  + _subtitleFiles.Where(f => SameLanguage(SubtitleFiles.Label(f), language)).TakeWhile(f => f != file).Count();
        if (!OpenSubtitleFile(file))
        {
            ShowNote("Couldn't open those subtitles");
            return;
        }
        ShowNote($"Subtitles: {language}");
        KeepChoice((_choice ?? new TrackChoice(null, 0, null, 0, false, 0)) with { Subtitles = language, SubtitleIndex = index, SubtitlesOff = false });
    }

    private async void PickSubtitleFile()
    {
        CloseMenu();
        var tag = Tag;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker { ViewMode = Windows.Storage.Pickers.PickerViewMode.List };
            foreach (var type in SubtitleFiles.Extensions) picker.FileTypeFilter.Add(type);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.Hwnd);
            var file = await picker.PickSingleFileAsync();
            _window.TakeFocus();
            if (file != null && !_closing && tag == Tag) UseSubtitleFile(file.Path);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            PlayerLog.Write($"subtitle file window: {e.Message}");
            ShowNote("Couldn't open the file window");
        }
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
        if (kind == "chapters") { GoToChapter(id); return; }   // 5.4.3
        if (kind == "online") { AddSubtitles(_foundSubtitles[id]); return; }
        if (id == PlayerMenu.FindOnline) { FindSubtitles(); return; }
        if (id == PlayerMenu.AddFile) { PickSubtitleFile(); return; }
        if (id <= PlayerMenu.FirstFile) { UseSubtitleFile(_subtitleFiles[PlayerMenu.FirstFile - id]); return; }
        var audio = kind == "audio";
        var tracks = Tracks(audio);
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
        _menu.ShowAt(r.Right - (int)Math.Round(_controls.MenuAnchorFromRight * scale), _controls.Top - (int)Math.Round(8 * scale), r, scale);
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
            if (_skips.Count > 0 && Player.IsPlaying) OfferSkip(Position, again: true);   // 5.3.0
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
            case 0x25: SeekBy(ctrl ? -BigSeekStep : -_backMs); break;                 // Left
            case 0x27: SeekBy(ctrl ? BigSeekStep : _forwardMs); break;                // Right
            case 0x4A: SeekBy(-_backMs); break;                                       // J (5.4.3)
            case 0x4C: SeekBy(_forwardMs); break;                                     // L
            case 0x4B: TogglePause(); break;                                          // K
            case 0x41: CycleTrack(audio: true); break;                                // A
            case 0x53: CycleTrack(audio: false); break;                               // S
            case 0x43: ShowChapters(); break;                                         // C
            case 0xBD or 0x6D: ChangeSpeed(-1); break;                                // -
            case 0xBB or 0x6B: ChangeSpeed(1); break;                                 // + (the = key too)
            case 0x49: SkipNow(); break;                                              // I
            case 0x56: SetClearVoices(!UiSettings.ClearVoices); break;                // V
            case PlayerWindow.VkBrowserBack: _ = CloseAsync(); break;                 // Alt+Left, the mouse's back button
            case 0xB3: MediaKeyHere(); TogglePause(); break;                          // media keys: play/pause
            case 0xB2: MediaKeyHere(); _ = CloseAsync(); break;                       // stop
            case 0xB0: MediaKeyHere(); GoNext(); break;                               // next
            case 0xB1: MediaKeyHere(); GoPrevious(); break;                           // previous
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
        if (_controls.Row == "stopped" && !_recovering) { TryAgain(); return; }   // 5.4.3: the file didn't answer
        var pausing = Player.IsPlaying;
        PlayerLog.Write(pausing ? "pause" : "play");
        // 5.4.3: after a long pause the picture can come back broken until the next key frame (the graphics
        // card's decoder, or a drive that went idle): a jump to the same place starts from a clean one
        if (!pausing && _pausedFor.Elapsed > LongPause && Player.IsSeekable) JumpTo(Position);
        if (pausing) _pausedFor.Restart();
        else _pausedFor.Reset();
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
        var from = Player.Time;
        var to = Math.Clamp(from + ms, 0, Math.Max(0, Player.Length - 1000));
        JumpTo(to);
        NoteJump(from, to);   // 5.3.0
        Feedback($"{(ms > 0 ? "+" : "-")}{Math.Abs(ms) / 1000} s  ({PlayerControls.Format(to)})");
    }

    /// <summary>What a key or the wheel did: the controls show it when they're up, else a small note.</summary>
    private void Feedback(string note)
    {
        if (_controls.Shown) ShowBar();
        else ShowNote(note);
    }

    private void ShowNote(string text, bool stay = false, int ms = 1500)
    {
        if (_closing) return;
        _note ??= PlayerLabel.Note(_window.Hwnd);
        var r = _window.ClientOnScreen();
        var scale = _window.Scale;
        var margin = (int)Math.Round(24 * scale);
        _note.ShowText(text, scale, (width, _) => new PointInt32(r.Right - width - margin, r.Top + margin));
        _noteHide.Stop();
        _noteHide.Interval = TimeSpan.FromMilliseconds(ms);   // 5.3.0: "Marked as watched" stays a little longer
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
        var stopped = _controls.Row == "stopped";   // 5.4.3: VLC is stopped then; where it was stays shown
        _controls.Update(stopped ? Position : Player.Time, stopped ? Length : Player.Length, Player.IsPlaying, Player.Mute, Volume, _window.IsFullScreen);
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
        foreach (var t in new[] { _hide, _tick, _click, _countdown, _noteTimer, _cursor, _health, _noteHide, _pauseWait, _credits }) t.Stop();
        PlayerLog.Write($"close at {PlayerControls.Format(Position)}");
        EndItem();
        _offered = null;
        CloseMenu();
        _note?.Close();
        _tip?.Close();
        _titleStrip?.Close();
        _pause?.Close();
        _controls.Close();
        _keys?.Close();   // 5.4.3
        _keys = null;
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
