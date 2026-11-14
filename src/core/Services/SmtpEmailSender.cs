using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Gradinware.Services
{
  public sealed class SmtpEmailSender : IEmailSender
  {
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
      _configuration = configuration;
    }

    public bool IsConfigured =>
      !string.IsNullOrWhiteSpace(_configuration["Email:SmtpHost"]) &&
      !string.IsNullOrWhiteSpace(_configuration["Email:From"]) &&
      !string.IsNullOrWhiteSpace(_configuration["PasswordReset:Url"]);

    public async Task SendPasswordResetAsync(string emailAddress, string resetUrl)
    {
      if (!IsConfigured)
      {
        throw new InvalidOperationException("Password reset email is not configured.");
      }

      var host = _configuration["Email:SmtpHost"];
      var port = int.TryParse(_configuration["Email:SmtpPort"], out var configuredPort) ? configuredPort : 587;
      var from = _configuration["Email:From"];
      using (var message = new MailMessage(from, emailAddress)
      {
        Subject = "Password reset",
        Body = $"Use this link to reset your password: {resetUrl}",
        IsBodyHtml = false,
      })
      using (var client = new SmtpClient(host, port)
      {
        EnableSsl = !bool.TryParse(_configuration["Email:UseSsl"], out var useSsl) || useSsl,
        DeliveryMethod = SmtpDeliveryMethod.Network,
      })
      {
        var username = _configuration["Email:Username"];
        var password = _configuration["Email:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
          client.Credentials = new NetworkCredential(username, password ?? string.Empty);
        }
        await client.SendMailAsync(message);
      }
    }
  }
}
