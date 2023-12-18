using System;
using System.Linq;
using System.Net;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers
{
  [ApiController]
  [Route("api/v1/password")]
  public class PasswordController : ControllerBase
  {
    [HttpPost]
    [Route("send-reset")]
    public ActionResult SendReset(SendPasswordResetEmailDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary())
        {
          StatusCode = (int) HttpStatusCode.BadRequest,
        };
      }

      string refreshToken = TokenGenerator.Generate(int.Parse(Startup.Configuration["PasswordReset:TokenLength"]));
      using (var db = new AccountContext())
      {
        var user = db.Users.FirstOrDefault(x => x.Email == dto.Email);
        if (user != null)
        {
          user.ResetTokens.Add(new ResetToken
          {
            Token = refreshToken,
          });
          db.SaveChanges();
        }

        // TODO: Send email
      }

      return Ok();
    }

    [HttpPost]
    [Route("reset")]
    public ActionResult ResetPassword(ResetPasswordDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary())
        {
          StatusCode = (int) HttpStatusCode.BadRequest,
        };
      }

      using (var db = new AccountContext())
      {
        var resetToken = db.ResetTokens.Include(rt => rt.User).FirstOrDefault(x => x.Token == dto.Token);
        if (resetToken == null)
        {
          return BadRequest();
        }

        resetToken.Used = true;
        db.SaveChanges();

        if (!dto.Password.MatchesPassword(resetToken.User.Password))
        {
          db.UserEvents.Add(new UserEvent
          {
            UserId = resetToken.User.Id,
            Code = UserEventCode.PasswordChanged,
            Timestamp = DateTime.UtcNow,
          });
          resetToken.User.Password = dto.Password.EncryptPassword();
          db.SaveChanges();
        }

        if (resetToken.User.RefreshTokens.Count > 0)
        {
          db.RefreshTokens.RemoveRange(resetToken.User.RefreshTokens);
          db.SaveChanges();
        }

        var grant = new OAuthPasswordGrant(resetToken.User.Id);
        var refreshToken = new RefreshToken(grant)
        {
          User = resetToken.User,
        };
        db.RefreshTokens.Add(refreshToken);
        db.SaveChanges();

        return new JsonResult(new OAuthPasswordGrantDto(grant));
      }
    }
  }
}
