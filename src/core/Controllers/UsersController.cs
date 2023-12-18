using System;
using System.Linq;
using System.Net;
using Gradinware.Attributes;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers
{
  [ApiController]
  [Route("api/v1/users")]
  public class UsersController : ControllerBase
  {

    [HttpPost]
    public ActionResult Post(CreateUserDto dto)
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
        User user = new User
        {
          FirstName = dto.FirstName,
          LastName = dto.LastName,
          Email = dto.Email,
          Password = dto.Password,
          DateCreated = DateTime.UtcNow,
          DateLastLogin = DateTime.UtcNow,
        };
        db.Users.Add(user);
        db.SaveChanges();

        db.UserEvents.Add(new UserEvent
        {
          UserId = user.Id,
          Code = UserEventCode.Created,
          Timestamp = DateTime.UtcNow,
        });

        var grant = new OAuthPasswordGrant(user.Id);
        var token = new RefreshToken(grant)
        {
          User = user,
        };
        db.RefreshTokens.Add(token);
        db.SaveChanges();

        return new JsonResult(new OAuthPasswordGrantDto(grant))
        {
          StatusCode = (int) HttpStatusCode.Created,
        };
      }
    }

    [HttpPut]
    [Route("me")]
    [AccessTokenAuthenticated]
    public ActionResult UpdateUser(UpdateUserDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary())
        {
          StatusCode = (int) HttpStatusCode.BadRequest,
        };
      }

      string token = Request.Headers["Authorization"];
      var values = token.Substring(7).DeserializeAccessToken();
      var userId = (long) values["userId"];
      using (var db = new AccountContext())
      {
        User user = db.Users.FirstOrDefault(x => x.Id == userId);
        if (user == null)
        {
          return BadRequest();
        }

        user.CopySharedProperties(dto);

        if (!string.IsNullOrEmpty(dto.Password) && !dto.Password.MatchesPassword(user.Password))
        {
          user.Password = user.Password.EncryptPassword();
          db.UserEvents.Add(new UserEvent
          {
            UserId = user.Id,
            Code = UserEventCode.PasswordChanged,
            Timestamp = DateTime.UtcNow,
          });
        }

        db.SaveChanges();
      }

      return Ok();
    }

    [HttpGet]
    [Route("me")]
    [AccessTokenAuthenticated]
    public ActionResult Me()
    {
      string token = Request.Headers["Authorization"];
      var values = token.Substring(7).DeserializeAccessToken();
      var userId = (long) values["userId"];
      using (var db = new AccountContext())
      {
        return new JsonResult(new UserDto(db.Users.First(x => x.Id == userId)));
      }
    }

    [HttpPost]
    [Route("sessions")]
    public ActionResult LogIn(LogInDto dto)
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
        var user = db.Users.FirstOrDefault(x => x.Email == dto.Email);
        if (user == null)
        {
          return BadRequest();
        }

        var grant = new OAuthPasswordGrant(user.Id);
        var token = new RefreshToken(grant)
        {
          User = user,
        };
        db.RefreshTokens.Add(token);
        db.SaveChanges();

        return new JsonResult(new OAuthPasswordGrantDto(grant));
      }
    }

    [HttpPost]
    [Route("token")]
    public ActionResult TokenExchange(TokenExchangeDto dto)
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
        var refreshToken = db.RefreshTokens.Include(rt => rt.User).FirstOrDefault(x => x.Token == dto.RefreshToken);
        if (refreshToken == null || refreshToken.IssuedAt.AddSeconds(int.Parse(Startup.Configuration["OAuth:RefreshTokenValidDuration"])) < DateTime.UtcNow)
        {
          return BadRequest();
        }

        var grant = new OAuthPasswordGrant(refreshToken.User.Id, dto.RefreshToken);
        return new JsonResult(new OAuthPasswordGrantDto(grant));
      }
    }
  }
}
