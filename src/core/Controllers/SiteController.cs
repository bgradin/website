using System.Linq;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers
{
  [ApiController]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  [Route("api/v1/site")]
  public class SiteController : ControllerBase
  {
    private readonly AccountContext _db;

    public SiteController(AccountContext db)
    {
      _db = db;
    }

    // A custom header keeps cookie-authenticated mutations limited to same-origin callers.
    private bool HasActionHeader => Request.Headers["X-Site-Action"] == "1";

    [HttpGet("config")]
    public IActionResult Config()
    {
      return Ok(new
      {
        captchaSiteKey = Startup.Configuration["Recaptcha:SiteKey"]
      });
    }

    [HttpGet("session")]
    public IActionResult Session()
    {
      var userId = SiteSession.UserId(HttpContext);
      var user = _db.Users.AsNoTracking()
        .Where(user => user.Id == userId)
        .Select(user => new { user.Id, user.FirstName, user.LastName, user.Email })
        .FirstOrDefault();

      Response.Headers.CacheControl = "no-store";
      return user == null ? Unauthorized() : Ok(new { user, isAdmin = SiteSession.IsAdmin(HttpContext) });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
      if (!HasActionHeader) return StatusCode(403);

      await HttpContext.SignOutAsync("site");
      return NoContent();
    }
  }
}
