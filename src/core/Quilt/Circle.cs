using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Circle : Patch
  {
    public const string NameKey = Constants.QuiltPropertyPrefix + "name";

    public string Name
    {
      get
      {
        return ContainsKey(NameKey)
          ? JsonUtility.LoadString(NameKey)
          : null;
      }
    }

    public Circle(JToken token)
      : base(token)
    {
    }
  }
}
