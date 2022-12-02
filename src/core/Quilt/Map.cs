using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Map : JObject
  {
    public void Insert(IEnumerable<string> path)
    {
      var segment = path.FirstOrDefault();
      if (!string.IsNullOrEmpty(segment))
      {
        if (!ContainsKey(segment))
        {
          this[segment] = new Map();
        }

        (this[segment] as Map).Insert(path.Skip(1));
      }
    }
  }
}
