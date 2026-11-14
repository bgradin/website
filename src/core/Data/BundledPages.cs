using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Quilting;

namespace Gradinware.Data
{
  internal static class BundledPages
  {
    private static readonly Lazy<IReadOnlyDictionary<string, JObject>> Pages = new(() => Read(typeof(BundledPages).Assembly));

    internal static IReadOnlyDictionary<string, JObject> Load() => Pages.Value;

    internal static IReadOnlyDictionary<string, JObject> Read(Assembly assembly)
    {
      const string prefix = "pages/";
      var pages = new Dictionary<string, JObject>(StringComparer.Ordinal);
      foreach (var name in assembly.GetManifestResourceNames())
      {
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal)) continue;
        var route = name.Substring(prefix.Length, name.Length - prefix.Length - ".json".Length).Replace('\\', '/');
        var key = Constants.ContentKey + "." + route;
        if (!KeyRules.IsValidPath(key, ".")) throw new InvalidOperationException("Invalid bundled page route: " + route);
        using var stream = assembly.GetManifestResourceStream(name);
        using var reader = new StreamReader(stream);
        pages.Add(key, JObject.Parse(reader.ReadToEnd()));
      }
      return pages;
    }
  }
}
