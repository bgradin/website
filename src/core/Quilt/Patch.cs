using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Patch : Dictionary<string, JToken>, IJsonConvertible
  {
    public const string CircleIdsKey = Constants.QuiltPropertyPrefix + "circleIds";

    public string[] CircleIds
    {
      get
      {
        return ContainsKey(CircleIdsKey)
          ? JsonUtility.LoadStringArray(this[CircleIdsKey])
          : null;
      }
      set
      {
        this[CircleIdsKey] = JArray.FromObject(value);
      }
    }

    public Patch(JToken token)
    {
      JsonUtility.VerifyType(token, JTokenType.Object);

      foreach (var kvp in (JObject) token)
      {
        this[kvp.Key] = kvp.Value;
      }
    }

    public static bool CanConvert(JToken token)
    {
      return token != null && token.Type == JTokenType.Object;
    }

    public virtual JToken ToJson()
    {
      return JToken.FromObject(
        new Dictionary<string, JToken>(
          this.Select(
            kvp => new KeyValuePair<string, JToken>(kvp.Key, kvp.Value)
          )
        )
      );
    }
  }
}
