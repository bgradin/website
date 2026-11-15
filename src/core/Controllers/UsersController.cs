using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Gradinware.Attributes;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Gradinware.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers
{
  [ApiController]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  [Route("api/v1/users")]
  public class UsersController : ControllerBase
  {
    private readonly AccountContext _db;
    private readonly IHttpClientFactory _httpClients;

    public UsersController(AccountContext db, IHttpClientFactory httpClients)
    {
      _db = db;
      _httpClients = httpClients;
    }

    [HttpPost]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> Post(CreateUserDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      var captchaSecret = Startup.Configuration["Recaptcha:SecretKey"];
      var expectedHostname = Startup.Configuration["Recaptcha:Hostname"];
      if (string.IsNullOrWhiteSpace(captchaSecret) || string.IsNullOrWhiteSpace(expectedHostname))
      {
        return StatusCode(503, new { error = "Registration verification is not configured." });
      }

      try
      {
        using var verificationData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
          ["secret"] = captchaSecret,
          ["response"] = dto.CaptchaToken ?? ""
        });
        using var response = await _httpClients.CreateClient("captcha").PostAsync(
          "https://www.google.com/recaptcha/api/siteverify", verificationData, HttpContext.RequestAborted);
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var verification = result.RootElement;
        if (!verification.TryGetProperty("success", out var success) || !success.GetBoolean() ||
            !verification.TryGetProperty("hostname", out var hostname) ||
            !string.Equals(hostname.GetString(), expectedHostname, StringComparison.OrdinalIgnoreCase))
        {
          return BadRequest(new { error = "Please complete the CAPTCHA again." });
        }
      }
      catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException)
      {
        return StatusCode(503, new { error = "Verification is unavailable. Please try again." });
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
      await HttpContext.SignInAsync("site", SiteSession.Principal(user.Id));
      return new JsonResult(new OAuthPasswordGrantDto(grant)) { StatusCode = (int)HttpStatusCode.Created };
    }

    [HttpPut]
    [Route("me")]
    public ActionResult UpdateUser(UpdateUserDto dto)
    {
      if (!ModelState.IsValid)
      {
        return new JsonResult(ModelState.ToDictionary()) { StatusCode = (int)HttpStatusCode.BadRequest };
      }

      var userId = SiteSession.UserId(HttpContext);
      if (!userId.HasValue) return Unauthorized();
      var user = _db.Users.FirstOrDefault(x => x.Id == userId);
      if (user == null)
      {
        return NotFound();
      }

      if (!Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal) && Request.Headers["X-Site-Action"] != "1")
        return StatusCode(403, new { error = "Please reload the page and try saving again." });
      dto.FirstName = dto.FirstName.Trim();
      dto.LastName = dto.LastName.Trim();
      dto.Email = dto.Email.Trim();
      if (dto.FirstName.Length < 2) ModelState.AddModelError("firstName", "First name must contain 2 to 35 characters.");
      if (dto.LastName.Length < 2) ModelState.AddModelError("lastName", "Last name must contain 2 to 35 characters.");
      if (!ModelState.IsValid) return ValidationProblem(ModelState);
      if (_db.Users.Any(x => x.Id != user.Id && x.Email.ToLower() == dto.Email.ToLower()))
        return Conflict(new { error = "This email address is already used by another account.", errors = new { email = new[] { "Choose a different email address." } } });

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
    public ActionResult Me()
    {
      var userId = SiteSession.UserId(HttpContext);
      if (!userId.HasValue) return Unauthorized();
      var user = _db.Users.FirstOrDefault(x => x.Id == userId);
      return user == null ? NotFound() : new JsonResult(new UserDto(user));
    }

    [HttpPost]
    [Route("sessions")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> LogIn(LogInDto dto)
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
      await HttpContext.SignInAsync("site", SiteSession.Principal(user.Id));
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
