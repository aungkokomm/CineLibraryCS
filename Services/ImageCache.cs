using System.Collections.Concurrent;

namespace CineLibraryCS.Services;

/// <summary>
/// Tiny LRU byte-cache for poster/thumbnail images.
///
/// Why: MovieCardControl / MovieRowControl / MovieDetailDialog all recycle as
/// the user scrolls and each realization previously did a fresh
/// File.ReadAllBytes off the disk (sometimes an external USB drive). With
/// ~1000 movies and fast scrolling that was the scrolling hot-path.
///
/// This cache stores raw bytes keyed by the cache-relative poster path.
/// Each control still builds its own BitmapImage at its own DecodePixelWidth
/// — that part is cheap once the bytes are in RAM.
///
/// Eviction: strict LRU bounded by total bytes (MaxBytes). Thread-safe.
/// Entries are immutable byte[] so we never copy on Get.
/// </summary>
public static class ImageCache
{
    // 80 MB — enough for a few hundred posters in typical libraries, tiny
    // compared to WinUI's own image decode cache.
    private const long MaxBytes = 80L * 1024 * 1024;

    // LinkedList gives O(1) touch-to-front + O(1) evict-from-tail.
    // Dictionary of key -> node gives O(1) lookup.
    private static readonly object _gate = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> _map = new();
    private static readonly LinkedList<Entry> _lru = new();
    private static long _bytes;

    private record Entry(string Key, byte[] Bytes);

    /// <summary>
    /// Returns cached bytes, or null if not cached. Promotes the entry to
    /// MRU on hit.
    /// </summary>
    public static byte[]? TryGet(string key)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Bytes;
            }
            return null;
        }
    }

    /// <summary>
    /// Stores bytes under key. Evicts LRU entries until total size fits.
    /// </summary>
    public static void Set(string key, byte[] bytes)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _bytes -= existing.Value.Bytes.Length;
                _lru.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<Entry>(new Entry(key, bytes));
            _lru.AddFirst(node);
            _map[key] = node;
            _bytes += bytes.Length;

            while (_bytes > MaxBytes && _lru.Last != null)
            {
                var tail = _lru.Last;
                _bytes -= tail.Value.Bytes.Length;
                _map.Remove(tail.Value.Key);
                _lru.RemoveLast();
            }
        }
    }

    /// <summary>
    /// Get-or-load helper: returns cached bytes or reads from disk and
    /// caches the result. Disk read is synchronous — callers run this from
    /// a Task.Run.
    /// </summary>
    public static byte[]? GetOrLoad(string key, string fullPath)
    {
        var cached = TryGet(key);
        if (cached != null) return cached;
        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            Set(key, bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public static void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
            _bytes = 0;
        }
    }
}
