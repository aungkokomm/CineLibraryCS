using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace CineLibraryCS.Helpers;

public static class StreamExtensions
{
    public static byte[] ReadAllBytes(this Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
