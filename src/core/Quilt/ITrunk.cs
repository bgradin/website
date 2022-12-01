using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public interface ITrunk
  {
    void Clear();
    IEnumerable<string> GetKeys();
    IEnumerable<string> GetKeys(string prefix);
    JToken Retrieve(string key);
    void Stow(string key, JToken value);
  }
}
