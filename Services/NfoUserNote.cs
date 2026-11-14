using System.Xml.Linq;

namespace CineLibraryCS.Services;

/// <summary>
/// 5.5.1: takes the &lt;user_note&gt; (tinyMediaManager's, CineLibrary Essentials') out of an .nfo
/// when the note it showed is deleted here, so a rescan doesn't bring it back. Every other tag
/// is left as it is; saved as the other .nfo writes are (<see cref="NfoRatings.Save"/>).
/// </summary>
public static class NfoUserNote
{
    /// <summary>False if the .nfo couldn't be read or written (not XML, read-only, the drive gone).</summary>
    public static bool Remove(string? nfo)
    {
        if (nfo == null || !File.Exists(nfo)) return true;
        lock (ImdbUpdate.Gate)
        {
            try
            {
                var doc = XDocument.Load(nfo);
                var notes = doc.Root?.Elements("user_note").ToList();
                if (notes is not { Count: > 0 }) return true;
                foreach (var note in notes) note.Remove();
                NfoRatings.Save(doc, nfo);
                return true;
            }
            catch { return false; }
        }
    }
}
