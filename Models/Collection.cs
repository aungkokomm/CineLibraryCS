namespace CineLibraryCS.Models;

public class Collection
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int MovieCount { get; set; }
}

public class GenreFacet
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public class LibraryStats
{
    public int TotalMovies { get; set; }
    public int TotalMissing { get; set; }
    public long TotalRuntime { get; set; }
    public double? AvgRating { get; set; }
    public int TotalDrives { get; set; }

    public string TotalRuntimeText
    {
        get
        {
            if (TotalRuntime == 0) return "—";
            var h = TotalRuntime / 60;
            if (h < 24) return $"{h}h";
            var d = h / 24;
            return $"{d}d {h % 24}h";
        }
    }

    public string AvgRatingText => AvgRating.HasValue ? $"★ {AvgRating:F1}" : "—";
}
