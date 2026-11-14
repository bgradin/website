using System.IO;

namespace Gradinware.Utility
{
  public static class DirectoryUtility
  {
    public static void EnsureDirectoryExists(string path)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(path));
    }

    public static string RemoveLeadingSeparator(string path)
    {
      return !string.IsNullOrEmpty(path) && (path[0] == Path.DirectorySeparatorChar || path[0] == Path.AltDirectorySeparatorChar)
        ? path.Substring(1) : path;
    }
  }
}
