using System.Linq;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Controllers
{
  [ApiController]
  [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
  [Route("api/v1/admin")]
  public class AdminController : ControllerBase
  {
    private const int UserPageSize = 50;
    private readonly AccountContext _db;

    public AdminController(AccountContext db)
    {
      _db = db;
    }

    // A custom header keeps cookie-authenticated mutations limited to same-origin callers.
    private bool HasActionHeader => Request.Headers["X-Site-Action"] == "1";

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] int page = 1)
    {
      if (!SiteSession.IsAdmin(HttpContext)) return StatusCode(403);
      if (page < 1 || page > 1_000_000) return BadRequest();

      Response.Headers.CacheControl = "no-store";
      var total = await _db.Users.CountAsync();
      var users = await _db.Users.AsNoTracking()
        .OrderBy(user => user.Id)
        .Skip((page - 1) * UserPageSize)
        .Take(UserPageSize)
        .Select(user => new
        {
          user.Id,
          user.FirstName,
          user.LastName,
          user.Email,
          user.DateCreated,
          user.DateLastLogin
        })
        .ToListAsync();

      return Ok(new { total, users });
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
      if (!SiteSession.IsAdmin(HttpContext) || !HasActionHeader) return StatusCode(403);
      if (id == SiteSession.UserId(HttpContext))
      {
        return Conflict(new { error = "You cannot delete your own administrator account." });
      }

      var user = await _db.Users.FindAsync(id);
      if (user == null) return NotFound();

      using var transaction = await _db.Database.BeginTransactionAsync();
      _db.RefreshTokens.RemoveRange(_db.RefreshTokens.Where(token => token.UserId == id));
      _db.ResetTokens.RemoveRange(_db.ResetTokens.Where(token => token.UserId == id));
      _db.UserEvents.RemoveRange(_db.UserEvents.Where(userEvent => userEvent.UserId == id));
      _db.Users.Remove(user);
      await _db.SaveChangesAsync();
      await transaction.CommitAsync();
      return NoContent();
    }

    [HttpPost("cache/clear")]
    public async Task<IActionResult> ClearCache()
    {
      if (!SiteSession.IsAdmin(HttpContext) || !HasActionHeader) return StatusCode(403);

      await HtmlCache.Clear();
      return Ok(new { message = "Site cache cleared." });
    }
  }
}
