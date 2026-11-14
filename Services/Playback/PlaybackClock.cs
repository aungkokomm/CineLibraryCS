namespace CineLibraryCS.Services.Playback;

/// <summary>
/// 5.4.3: where a video really is: VLC's clock without a jump nobody asked for, like the one it can make when
/// a read fails just before it gives up. Small steps are playing; a big one counts when it was asked for (a
/// seek, a skip, a chapter) or once the video plays on steadily from there for two seconds. VLC's clock comes
/// on its own thread, the rest on the UI thread. Times in ms; "now" in ticks (Environment.TickCount64).
/// </summary>
internal sealed class PlaybackClock
{
    /// <summary>A step of VLC's clock bigger than this that nobody asked for isn't playing.</summary>
    public const long LargestStep = 10_000;
    private const long Steady = 2000, AskedFor = 3000, StartGrace = 5000;
    private readonly object _lock = new();
    private long _position, _candidate = -1, _candidateSince, _askedUntil;

    public long Position { get { lock (_lock) return _position; } }

    /// <summary>A video starts here: VLC's first time may land a little before or after it.</summary>
    public void Start(long ms, long now)
    {
        lock (_lock) { _position = ms; _candidate = -1; _askedUntil = now + StartGrace; }
    }

    /// <summary>A jump was asked for, to a place VLC works out (a chapter): its clock gets there a moment later.</summary>
    public void Asked(long now)
    {
        lock (_lock) _askedUntil = now + AskedFor;
    }

    /// <summary>A jump asked for, to a known place.</summary>
    public void Jump(long ms, long now)
    {
        lock (_lock) { _position = ms; _candidate = -1; _askedUntil = now + AskedFor; }
    }

    /// <summary>VLC's clock said this.</summary>
    public void Track(long time, long now)
    {
        lock (_lock)
        {
            if (Math.Abs(time - _position) <= LargestStep || now < _askedUntil)
            {
                _position = time;
                _candidate = -1;
                return;
            }
            if (_candidate < 0 || Math.Abs(time - _candidate) > LargestStep) _candidateSince = now;
            _candidate = time;
            if (now - _candidateSince < Steady) return;
            _position = time;
            _candidate = -1;
        }
    }
}
