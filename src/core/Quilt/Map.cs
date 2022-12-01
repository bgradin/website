using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Map : Dictionary<string, Map>, IJsonConvertible
  {
    public JToken ToJson()
    {
      return Keys.Aggregate(new JObject(), (obj, key) =>
      {
        obj[key] = this[key].ToJson();
        return obj;
      });
    }

    public void Insert(IEnumerable<string> path)
    {
      var segment = path.FirstOrDefault();
      if (!string.IsNullOrEmpty(segment))
      {
        if (!ContainsKey(segment))
        {
          this[segment] = new Map();
        }

        this[segment].Insert(path.Skip(1));
      }
    }
  }
}
