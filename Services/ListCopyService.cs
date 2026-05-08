using CineLibraryCS.Models;

namespace CineLibraryCS.Services;

/// <summary>
/// Bucket-style export: copies the on-disk folder of every movie in a user
/// list to a destination directory, preserving each movie's folder layout
/// (video + .nfo + posters + everything inside). Source files are never
/// touched. Offline drives are skipped and reported in the summary.
/// </summary>
public class ListCopyService
{
    public enum ConflictPolicy { Ask, Skip, Overwrite }

    public record CopyItem(
        int MovieId,
        string Title,
        string SourceFolder,        // absolute, e.g. "H:\Movies\Inception (2010)"
        string DestFolderName,      // last segment, e.g. "Inception (2010)"
        long Bytes,                 // total bytes in source folder
        int FileCount);

    public record CopyPlan(
        List<CopyItem> Items,
        long TotalBytes,
        int TotalFiles,
        List<string> OfflineDriveLabels);   // distinct labels for skipped offline drives

    public record CopyProgress(
        int MoviesDone,
        int MoviesTotal,
        long BytesDone,
        long BytesTotal,
        string CurrentFile);

    public record CopyResult(int Copied, int Skipped, int OfflineSkipped, bool Cancelled);

    private readonly DatabaseService _db;
    public ListCopyService(DatabaseService db) => _db = db;

    /// <summary>
    /// Build a CopyPlan: figure out which list movies are online, where their
    /// folders live, how much disk space they consume in total. Folder walks
    /// are synchronous — caller should run from background thread for big lists.
    /// </summary>
    public CopyPlan BuildPlan(int listId, Dictionary<string, string> connected)
    {
        var sources = _db.GetMoviesForCopy(listId);
        var items = new List<CopyItem>();
        var offlineLabels = new HashSet<string>();
        long totalBytes = 0;
        int totalFiles = 0;

        foreach (var src in sources)
        {
            if (!connected.TryGetValue(src.VolumeSerial, out var letter))
            {
                offlineLabels.Add(LookupDriveLabel(src.VolumeSerial));
                continue;
            }

            var srcFolder = Path.Combine($"{letter}:\\", src.FolderRelPath.Replace('/', '\\'));
            if (!Directory.Exists(srcFolder))
            {
                offlineLabels.Add(LookupDriveLabel(src.VolumeSerial));
                continue;
            }

            long bytes = 0; int files = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(srcFolder, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; files++; } catch { }
                }
            }
            catch { /* unreadable folder — fall through with bytes=0 */ }

            var folderName = new DirectoryInfo(srcFolder).Name;
            items.Add(new CopyItem(src.Id, src.Title, srcFolder, folderName, bytes, files));
            totalBytes += bytes;
            totalFiles += files;
        }

        return new CopyPlan(items, totalBytes, totalFiles, offlineLabels.OrderBy(s => s).ToList());
    }

    private string LookupDriveLabel(string serial)
    {
        // Best effort — falls back to the serial when the drives table is
        // unreachable (shouldn't happen, but don't let a UI hint kill the plan).
        try
        {
            foreach (var d in _db.GetDrives())
                if (d.VolumeSerial == serial) return d.Label;
        }
        catch { }
        return serial;
    }

    /// <summary>
    /// Returns names of plan items whose target folder already exists at
    /// destRoot. Used to gate the "Skip / Overwrite" conflict prompt.
    /// </summary>
    public List<string> FindExistingTargets(CopyPlan plan, string destRoot)
    {
        var hits = new List<string>();
        foreach (var item in plan.Items)
        {
            var dest = Path.Combine(destRoot, item.DestFolderName);
            if (Directory.Exists(dest)) hits.Add(item.DestFolderName);
        }
        return hits;
    }

    /// <summary>
    /// Returns free bytes available on the volume of destRoot, or -1 if
    /// it can't be determined. Caller compares against plan.TotalBytes.
    /// </summary>
    public long GetFreeBytes(string destRoot)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destRoot));
            if (string.IsNullOrEmpty(root)) return -1;
            var di = new System.IO.DriveInfo(root);
            return di.AvailableFreeSpace;
        }
        catch { return -1; }
    }

    /// <summary>
    /// Execute the plan. Reports progress per file. Honors cancellation
    /// between files (mid-file copy completes before checking).
    /// </summary>
    public async Task<CopyResult> ExecuteAsync(
        CopyPlan plan,
        string destRoot,
        ConflictPolicy conflict,
        IProgress<CopyProgress> progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            int copied = 0, skipped = 0;
            long bytesDone = 0;

            Directory.CreateDirectory(destRoot);

            for (int i = 0; i < plan.Items.Count; i++)
            {
                if (ct.IsCancellationRequested) return new CopyResult(copied, skipped, plan.OfflineDriveLabels.Count, true);

                var item = plan.Items[i];
                var destFolder = Path.Combine(destRoot, item.DestFolderName);

                if (Directory.Exists(destFolder))
                {
                    if (conflict == ConflictPolicy.Skip)
                    {
                        skipped++;
                        bytesDone += item.Bytes;
                        progress.Report(new CopyProgress(i + 1, plan.Items.Count, bytesDone, plan.TotalBytes, $"Skipped: {item.Title}"));
                        continue;
                    }
                    // Overwrite: leave existing folder in place; we just copy
                    // files on top, replacing matching paths. Files that exist
                    // in dest but not in source are left alone (intentional —
                    // we don't delete user content).
                }
                Directory.CreateDirectory(destFolder);

                try
                {
                    foreach (var srcFile in Directory.EnumerateFiles(item.SourceFolder, "*", SearchOption.AllDirectories))
                    {
                        if (ct.IsCancellationRequested) return new CopyResult(copied, skipped, plan.OfflineDriveLabels.Count, true);

                        var rel = Path.GetRelativePath(item.SourceFolder, srcFile);
                        var destFile = Path.Combine(destFolder, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                        // For overwrite or fresh, just copy. File.Copy with
                        // overwrite=true handles read-only attribute? No — but
                        // chances of read-only on .nfo/poster are slim. If we
                        // hit one, ClearReadOnly + retry once.
                        try
                        {
                            File.Copy(srcFile, destFile, overwrite: true);
                        }
                        catch (UnauthorizedAccessException) when (File.Exists(destFile))
                        {
                            try
                            {
                                var fi = new FileInfo(destFile);
                                if (fi.IsReadOnly) fi.IsReadOnly = false;
                                File.Copy(srcFile, destFile, overwrite: true);
                            }
                            catch { /* give up on this file, continue */ }
                        }

                        long bytes = 0;
                        try { bytes = new FileInfo(srcFile).Length; } catch { }
                        bytesDone += bytes;
                        progress.Report(new CopyProgress(i + 1, plan.Items.Count, bytesDone, plan.TotalBytes, rel));
                    }
                    copied++;
                }
                catch (OperationCanceledException) { return new CopyResult(copied, skipped, plan.OfflineDriveLabels.Count, true); }
                catch
                {
                    skipped++;
                    // continue with next movie
                }
            }
            return new CopyResult(copied, skipped, plan.OfflineDriveLabels.Count, false);
        }, ct);
    }
}
