using System.Threading.Tasks;

namespace Gradinware.Services
{
  public interface IEmailSender
  {
    bool IsConfigured { get; }
    Task SendPasswordResetAsync(string emailAddress, string resetUrl);
  }
}
