using System.Text;
namespace OpenWiimote.Core.Storage;

/// <summary>Serialize writers to each path and let Windows replace files while our readers hold them.</summary>
internal static class AtomicTextFile
{
    // Fixed stripes cover separate store instances without retaining arbitrary file paths forever.
    private static readonly object[] Gates = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
    internal static void Write(string path, string contents, bool overwrite = true)
    {
        var normalized = Path.GetFullPath(path);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var gate = Gates[(uint)comparer.GetHashCode(normalized) % Gates.Length];
        lock (gate)
        {
            var temporary = normalized + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(contents); writer.Flush(); stream.Flush(flushToDisk: true);
                }
                // ReplaceFile preserves an open reader's old file while installing the
                // complete replacement. MoveFileEx replacement can leave the Windows
                // destination delete-pending until the old reader closes.
                if (overwrite && File.Exists(normalized)) File.Replace(temporary, normalized, null);
                else File.Move(temporary, normalized, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal static string Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
