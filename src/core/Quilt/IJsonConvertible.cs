using Newtonsoft.Json.Linq;

namespace Quilting
{
  public interface IJsonConvertible
  {
    JToken ToJson();
  }
}
