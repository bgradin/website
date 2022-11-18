using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gradinware.Utility
{
  public static class JsonUtility
  {
    public static bool IsValid(string json)
    {
      try
      {
        JObject.Parse(json);
      }
      catch (JsonReaderException)
      {
        return false;
      }

      return true;
    }
  }
}
