using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public static class Extensions
  {
    public static bool ContainsKey(this JToken token, string key)
    {
      return token.SelectToken(key) != null;
    }

    public static IEnumerable<T> EmptyIfNull<T>(this IEnumerable<T> enumerable)
    {
      return enumerable ?? Array.Empty<T>();
    }

    // Sequence XOR
    public static IEnumerable<T> Exclusion<T>(this IEnumerable<T> sequence1, IEnumerable<T> sequence2)
    {
      return sequence1.Except(sequence2).Union(sequence2.Except(sequence1));
    }
  }
}
