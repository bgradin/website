using System.Threading.Tasks;

namespace Gradinware.Services
{
  public interface IReactSsrClient
  {
    Task<string> Render(string json);
  }
}
