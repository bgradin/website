using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Quilting
{
  internal sealed class ArrayAddress
  {
    public string ContainerKey { get; }
    public string TokenPath { get; }

    private ArrayAddress(string containerKey, string tokenPath)
    {
      ContainerKey = containerKey;
      TokenPath = tokenPath;
    }

    public static ArrayAddress Parse(string key, string delimiter)
    {
      if (string.IsNullOrWhiteSpace(key) || string.IsNullOrEmpty(delimiter)) return null;
      var segments = key.Split(new[] { delimiter }, StringSplitOptions.None);
      for (var i = 0; i < segments.Length; i++)
      {
        if (!segments[i].Contains("[")) continue;
        var head = segments[i].Split('[')[0];
        var container = string.Join(delimiter, segments.Take(i).Concat(new[] { head }));
        var tail = segments.Skip(i).ToArray();
        tail[0] = tail[0].Substring(head.Length);
        var path = string.Join(".", tail);
        return new ArrayAddress(container, path);
      }
      return null;
    }
  }

  /// <summary>Validation rules for names used in persisted Quilt paths.</summary>
  public static class KeyRules
  {
    public static bool IsValidSegment(string value, string delimiter)
    {
      return !string.IsNullOrWhiteSpace(value)
        && !string.IsNullOrEmpty(delimiter)
        && !value.Contains(delimiter)
        && value.IndexOfAny(new[] { '[', ']' }) < 0;
    }

    public static bool IsValidPath(string value, string delimiter)
    {
      return !string.IsNullOrWhiteSpace(value)
        && !string.IsNullOrEmpty(delimiter)
        && value.Split(new[] { delimiter }, StringSplitOptions.None)
          .All(segment => Regex.IsMatch(segment, @"^[^\[\]]+(?:\[\d+\])*$")
            && !string.IsNullOrWhiteSpace(segment)
            && !segment.Contains(delimiter));
    }
  }
}
