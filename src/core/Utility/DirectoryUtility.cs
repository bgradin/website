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
      return path[0] == Path.DirectorySeparatorChar ? path.Substring(1) : path;
    }
  }
}
