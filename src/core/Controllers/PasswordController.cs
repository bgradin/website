using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Gradinware.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.RateLimiting;

namespace Gradinware.Controllers
{
  [ApiController]
  [Route("api/v1/password")]
  public class PasswordController : ControllerBase
  {
    private readonly AccountContext _db;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PasswordController> _logger;

    public PasswordController(AccountContext db, IEmailSender emailSender, IConfiguration configuration, ILogger<PasswordController> logger)
    {
      _db = db;
      _emailSender = emailSender;
      _configuration = configuration;
      _logger = logger;
    }

    [HttpPost]
    [Route("send-reset")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> SendReset(SendPasswordResetEmailDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }
      if (!_emailSender.IsConfigured)
      {
        return StatusCode((int)HttpStatusCode.ServiceUnavailable);
      }

      var user = _db.Users.FirstOrDefault(x => x.Email == dto.Email);
      if (user != null)
      {
        var rawToken = TokenGenerator.Generate(int.Parse(_configuration["PasswordReset:TokenLength"]));
        var resetToken = new ResetToken
        {
          User = user,
          Token = TokenGenerator.Hash(rawToken),
          ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
        var previousTokens = _db.ResetTokens.Where(item => item.UserId == user.Id && !item.Used).ToList();
        _db.ResetTokens.RemoveRange(previousTokens);
        _db.ResetTokens.Add(resetToken);
        _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.PasswordResetRequested, Timestamp = DateTime.UtcNow });
        _db.SaveChanges();

        var resetUrl = _configuration["PasswordReset:Url"];
        var separator = resetUrl.Contains("?") ? "&" : "?";
        var encodedToken = Uri.EscapeDataString(rawToken);
        try
        {
          await _emailSender.SendPasswordResetAsync(user.Email, $"{resetUrl}{separator}token={encodedToken}");
        }
        catch (Exception exception)
        {
          _logger.LogError(exception, "Could not send a password reset email.");
          resetToken.Used = true;
          _db.SaveChanges();
        }
      }

      // Same success response whether the address is registered or not.
      return Ok();
    }

    [HttpPost]
    [Route("reset")]
    [EnableRateLimiting("auth")]
    public ActionResult ResetPassword(ResetPasswordDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      using (var transaction = _db.Database.BeginTransaction())
      {
        var now = DateTime.UtcNow;
        var resetToken = _db.ResetTokens
          .Include(rt => rt.User)
          .Where(rt => !rt.Used && rt.ExpiresAt > now)
          .FirstOrDefault(rt => rt.Token == TokenGenerator.Hash(dto.Token));
        if (resetToken == null)
        {
          return BadRequest();
        }

        resetToken.Used = true;
        resetToken.User.Password = dto.Password.EncryptPassword();
        _db.UserEvents.Add(new UserEvent { UserId = resetToken.User.Id, Code = UserEventCode.PasswordResetCompleted, Timestamp = now });
        var otherResetTokens = _db.ResetTokens.Where(item => item.UserId == resetToken.UserId && item.Id != resetToken.Id).ToList();
        _db.ResetTokens.RemoveRange(otherResetTokens);

        var refreshTokens = _db.RefreshTokens.Where(rt => rt.UserId == resetToken.UserId).ToList();
        _db.RefreshTokens.RemoveRange(refreshTokens);
        var grant = new OAuthPasswordGrant(resetToken.User.Id);
        _db.RefreshTokens.Add(new RefreshToken(grant) { User = resetToken.User });
        _db.SaveChanges();
        transaction.Commit();
        return new JsonResult(new OAuthPasswordGrantDto(grant));
      }
    }
  }
}
