using System;
using System.Linq;
using System.Net;
using Gradinware.Attributes;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace Gradinware.Controllers
{
  [ApiController]
  [Route("api/v1/users")]
  public class UsersController : ControllerBase
  {
    private readonly AccountContext _db;

    public UsersController(AccountContext db)
    {
      _db = db;
    }

    [HttpPost]
    [EnableRateLimiting("auth")]
    public ActionResult Post(CreateUserDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      if (_db.Users.Any(x => x.Email == dto.Email))
      {
        return Conflict();
      }

      var user = new User
      {
        FirstName = dto.FirstName,
        LastName = dto.LastName,
        Email = dto.Email,
        Password = dto.Password.EncryptPassword(),
        DateCreated = DateTime.UtcNow,
        DateLastLogin = DateTime.UtcNow,
      };
      _db.Users.Add(user);
      _db.SaveChanges();
      _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.Created, Timestamp = DateTime.UtcNow });

      var grant = new OAuthPasswordGrant(user.Id);
      _db.RefreshTokens.Add(new RefreshToken(grant) { User = user });
      _db.SaveChanges();
      return new JsonResult(new OAuthPasswordGrantDto(grant)) { StatusCode = (int)HttpStatusCode.Created };
    }

    [HttpPut]
    [Route("me")]
    [AccessTokenAuthenticated]
    public ActionResult UpdateUser(UpdateUserDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      var values = Request.Headers["Authorization"].ToString().Substring(7).DeserializeAccessToken();
      var userId = Convert.ToInt32(values["userId"]);
      var user = _db.Users.FirstOrDefault(x => x.Id == userId);
      if (user == null)
      {
        return NotFound();
      }

      user.FirstName = dto.FirstName;
      user.LastName = dto.LastName;
      user.Email = dto.Email;
      if (!string.IsNullOrEmpty(dto.Password) && !dto.Password.MatchesPassword(user.Password))
      {
        user.Password = dto.Password.EncryptPassword();
        _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.PasswordChanged, Timestamp = DateTime.UtcNow });
        var refreshTokens = _db.RefreshTokens.Where(item => item.UserId == user.Id).ToList();
        _db.RefreshTokens.RemoveRange(refreshTokens);
      }
      _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.Updated, Timestamp = DateTime.UtcNow });
      _db.SaveChanges();
      return Ok();
    }

    [HttpGet]
    [Route("me")]
    [AccessTokenAuthenticated]
    public ActionResult Me()
    {
      var values = Request.Headers["Authorization"].ToString().Substring(7).DeserializeAccessToken();
      var userId = Convert.ToInt32(values["userId"]);
      var user = _db.Users.FirstOrDefault(x => x.Id == userId);
      return user == null ? NotFound() : new JsonResult(new UserDto(user));
    }

    [HttpPost]
    [Route("sessions")]
    [EnableRateLimiting("auth")]
    public ActionResult LogIn(LogInDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      var user = _db.Users.FirstOrDefault(x => x.Email == dto.Email);
      if (user == null || !dto.Password.MatchesPassword(user.Password))
      {
        if (user != null)
        {
          _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.LoginFailed, Timestamp = DateTime.UtcNow });
          _db.SaveChanges();
        }
        return BadRequest();
      }

      if (!user.Password.IsPasswordHash())
      {
        user.Password = dto.Password.EncryptPassword();
      }
      user.DateLastLogin = DateTime.UtcNow;
      _db.UserEvents.Add(new UserEvent { UserId = user.Id, Code = UserEventCode.LoginSucceeded, Timestamp = DateTime.UtcNow });

      var grant = new OAuthPasswordGrant(user.Id);
      _db.RefreshTokens.Add(new RefreshToken(grant) { User = user });
      _db.SaveChanges();
      return new JsonResult(new OAuthPasswordGrantDto(grant));
    }

    [HttpPost]
    [Route("token")]
    public ActionResult TokenExchange(TokenExchangeDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      var refreshToken = _db.RefreshTokens.Include(rt => rt.User).FirstOrDefault(x => x.Token == dto.RefreshToken);
      if (refreshToken == null || refreshToken.IssuedAt.AddSeconds(int.Parse(Startup.Configuration["OAuth:RefreshTokenValidDuration"])) < DateTime.UtcNow)
      {
        return BadRequest();
      }

      return new JsonResult(new OAuthPasswordGrantDto(new OAuthPasswordGrant(refreshToken.User.Id, dto.RefreshToken)));
    }
  }
}
