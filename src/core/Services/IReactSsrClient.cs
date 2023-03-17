using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Gradinware.Services
{
  public interface IReactSsrClient
  {
    Task<string> Render(JToken json);
  }
}
