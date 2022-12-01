using System.Linq;
using Newtonsoft.Json.Linq;
using Quilting.Exceptions;

namespace Quilting
{
  internal static class JsonUtility
  {
    public static string LoadString(JToken token, string key)
    {
      return Load<string>(token, key, JTokenType.String);
    }

    public static string LoadString(JToken token)
    {
      return Load<string>(token, JTokenType.String);
    }

    public static int LoadInteger(JToken token, string key)
    {
      return Load<int>(token, key, JTokenType.Integer);
    }

    public static int LoadInteger(JToken token)
    {
      return Load<int>(token, JTokenType.Integer);
    }

    public static bool LoadBoolean(JToken token, string key)
    {
      return Load<bool>(token, key, JTokenType.Boolean);
    }

    public static bool LoadBoolean(JToken token)
    {
      return Load<bool>(token, JTokenType.Boolean);
    }

    public static double LoadFloat(JToken token, string key)
    {
      return Load<float>(token, key, JTokenType.Float);
    }

    public static double LoadFloat(JToken token)
    {
      return Load<float>(token, JTokenType.Float);
    }

    public static string[] LoadStringArray(JToken token, string key)
    {
      return LoadArray<string>(token, key, JTokenType.String);
    }

    public static string[] LoadStringArray(JToken token)
    {
      return LoadArray<string>(token, JTokenType.String);
    }

    public static int[] LoadIntegerArray(JToken token, string key)
    {
      return LoadArray<int>(token, key, JTokenType.Integer);
    }

    public static int[] LoadIntegerArray(JToken token)
    {
      return LoadArray<int>(token, JTokenType.Integer);
    }

    public static bool[] LoadBooleanArray(JToken token, string key)
    {
      return LoadArray<bool>(token, key, JTokenType.Boolean);
    }

    public static bool[] LoadBooleanArray(JToken token)
    {
      return LoadArray<bool>(token, JTokenType.Boolean);
    }

    public static double[] LoadFloatArray(JToken token, string key)
    {
      return LoadArray<double>(token, key, JTokenType.Float);
    }

    public static double[] LoadFloatArray(JToken token)
    {
      return LoadArray<double>(token, JTokenType.Float);
    }

    public static T Load<T>(JToken token, string key, JTokenType type)
    {
      VerifyType(token, JTokenType.Object);
      return Load<T>(token.SelectToken(key), type);
    }

    public static T Load<T>(JToken token, JTokenType type)
    {
      VerifyType(token, type);
      return token.Value<T>();
    }

    public static T[] LoadArray<T>(JToken token, string key, JTokenType type)
    {
      VerifyType(token, JTokenType.Object);
      return LoadArray<T>(token.SelectToken(key), type);
    }

    public static T[] LoadArray<T>(JToken token, JTokenType type)
    {
      VerifyType(token, JTokenType.Array);
      return (token as JArray).Select(x =>
      {
        VerifyType(x, type);
        return x.Value<T>();
      }).ToArray();
    }

    public static void VerifyType(JToken token, JTokenType type)
    {
      if (token == null || token.Type != type)
      {
        throw new InvalidTypeException();
      }
    }
  }
}
