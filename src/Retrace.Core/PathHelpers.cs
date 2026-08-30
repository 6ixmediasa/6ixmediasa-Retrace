using System.Security.Cryptography;
using System.Text;

namespace Retrace.Core;

public static class PathHelpers
{
    public static string RootKey(string root)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }

    public static bool IsUnder(string path, string root)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string SafeRelative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.StartsWith("..")) throw new InvalidOperationException("Path escaped watched root.");
        return relative;
    }

    public static bool TryCopyFile(string source, string destination, long maxBytes)
    {
        try
        {
            var info = new FileInfo(source);
            if (!info.Exists || info.Length > maxBytes) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            File.SetLastWriteTimeUtc(destination, info.LastWriteTimeUtc);
            return true;
        }
        catch { return false; }
    }
}
