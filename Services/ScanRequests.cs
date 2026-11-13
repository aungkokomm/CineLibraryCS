namespace CineLibraryCS.Services;

/// <summary>
/// 5.4.0: folders CineLibrary Essentials asks to have scanned ("Scan in
/// CineLibrary"). Essentials leaves a text file, one folder per line, in
/// CineLibrary-Data\requests; this reads the ones waiting at start-up and new
/// ones while the app runs, then deletes them. Nothing is registered anywhere.
/// </summary>
public sealed class ScanRequests : IDisposable
{
    private readonly string _folder;
    private readonly Action<List<string>> _onRequest;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;

    /// <param name="onRequest">Gets the folders asked for; called off the UI thread.</param>
    public ScanRequests(string dataDir, Action<List<string>> onRequest)
    {
        _folder = Path.Combine(dataDir, "requests");
        _onRequest = onRequest;
    }

    public void Start()
    {
        Directory.CreateDirectory(_folder);
        // Essentials writes a .tmp and renames it to .txt once it's complete
        _watcher = new FileSystemWatcher(_folder, "*.txt") { NotifyFilter = NotifyFilters.FileName };
        _watcher.Created += (_, _) => Collect();
        _watcher.Renamed += (_, _) => Collect();
        _watcher.EnableRaisingEvents = true;
        Task.Run(Collect);   // any left from before the app started
    }

    private void Collect()
    {
        var folders = new List<string>();
        lock (_gate)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(_folder, "*.txt"))
                {
                    try
                    {
                        folders.AddRange(File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0));
                        File.Delete(file);
                    }
                    catch { /* still being written: the next event reads it */ }
                }
            }
            catch { /* the folder went away */ }
        }
        var distinct = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 0) _onRequest(distinct);
    }

    public void Dispose() => _watcher?.Dispose();

    // More folders than this under one of CineLibrary's scan folders: that whole
    // folder is scanned once instead (the scan skips .nfo files that haven't changed)
    private const int WholeRootFrom = 20;

    /// <summary>
    /// What to scan for the folders asked for: each one inside a scan folder of a
    /// connected drive is scanned itself; asking for a folder that holds scan
    /// folders scans those. <paramref name="notCovered"/> gets the folders that
    /// aren't on a drive CineLibrary knows or aren't in a folder it scans, since
    /// scanning those would only add titles the next refresh takes away again.
    /// </summary>
    public static List<(string Serial, string DriveRoot, string ScanFolder)> Plan(
        IEnumerable<string> folders, IEnumerable<Models.DriveInfo> drives, IReadOnlyDictionary<string, string> connected,
        out List<string> notCovered)
    {
        notCovered = new List<string>();
        var found = new List<(string Serial, string DriveRoot, string Root, string Folder)>();
        var driveList = drives.ToList();

        foreach (var asked in folders)
        {
            string folder;
            try { folder = Normalize(Path.GetFullPath(asked)); }
            catch { notCovered.Add(asked); continue; }

            var drive = driveList.FirstOrDefault(d => connected.TryGetValue(d.VolumeSerial, out var letter)
                && string.Equals($"{letter}:", folder[..2], StringComparison.OrdinalIgnoreCase));
            if (drive == null)
            {
                notCovered.Add(asked);
                continue;
            }
            var driveRoot = $"{connected[drive.VolumeSerial]}:\\";
            var roots = drive.Folders
                .Select(r => Normalize(Path.Combine(driveRoot, r.RootPath.Replace('/', '\\'))))
                .Where(Directory.Exists)
                .ToList();

            var within = roots.FirstOrDefault(r => IsInside(folder, r));
            if (within != null)
            {
                found.Add((drive.VolumeSerial, driveRoot, within, folder));
                continue;
            }
            var holds = roots.Where(r => IsInside(r, folder)).ToList();
            if (holds.Count == 0) notCovered.Add(asked);
            foreach (var r in holds) found.Add((drive.VolumeSerial, driveRoot, r, r));
        }

        var work = new List<(string Serial, string DriveRoot, string ScanFolder)>();
        foreach (var group in found.GroupBy(f => (f.Serial, f.Root)))
        {
            var items = group.Select(f => f.Folder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var first = group.First();
            if (items.Count >= WholeRootFrom || items.Any(f => string.Equals(f, first.Root, StringComparison.OrdinalIgnoreCase)))
            {
                work.Add((first.Serial, first.DriveRoot, first.Root));
                continue;
            }
            // A folder inside another one asked for is scanned with it
            foreach (var f in items.Where(f => !items.Any(o => o != f && IsInside(f, o))))
                work.Add((first.Serial, first.DriveRoot, f));
        }
        return work;
    }

    /// <summary>No trailing backslash, except on a drive root ("E:\").</summary>
    private static string Normalize(string path)
    {
        var p = path.TrimEnd('\\');
        return p.Length == 2 && p[1] == ':' ? p + "\\" : p;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or somewhere under it.</summary>
    private static bool IsInside(string path, string folder)
        => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(folder.EndsWith('\\') ? folder : folder + "\\", StringComparison.OrdinalIgnoreCase);
}
