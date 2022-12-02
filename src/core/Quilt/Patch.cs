using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Patch : JObject
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

    public static Patch TryConvert(JToken token)
    {
      return CanConvert(token) ? new Patch(token) : null;
    }
  }
}
