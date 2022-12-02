using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public static class Extensions
  {
    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T> enumerable)
    {
      return enumerable ?? Array.Empty<T>();
    }

    // Sequence XOR
    public static IEnumerable<T> ExclusiveUnion<T>(this IEnumerable<T> source, IEnumerable<T> other)
    {
      return source.Union(other).Except(source.Intersect(other));
    }

    public static string[] GetKeys(this JObject source)
    {
      return (source as IEnumerable<KeyValuePair<string, JToken>>).Select(x => x.Key).ToArray();
    }
  }
}
