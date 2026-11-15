using System.Linq;
using System.Net;
using System;
using Gradinware.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Gradinware.Attributes
{
  public class AccessTokenAuthenticatedAttribute : ActionFilterAttribute
  {
    public AccessTokenAuthenticatedAttribute()
    {
      Scope = "user";
    }

    public string Scope { get; set; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
      if (!context.HttpContext.Request.Headers.ContainsKey(_authorizationHeaderName)
        || !AuthorizationIsValid(context.HttpContext.Request.Headers[_authorizationHeaderName]))
      {
        context.Result = new JsonResult("unauthorized")
        {
          StatusCode = (int) HttpStatusCode.Unauthorized,
        };
      }
      else
      {
        var token = context.HttpContext.Request.Headers[_authorizationHeaderName].ToString().Substring(7);
        var userId = Convert.ToInt32(token.DeserializeAccessToken()["userId"]);
        var db = context.HttpContext.RequestServices.GetRequiredService<AccountContext>();
        if (!db.Users.Any(user => user.Id == userId))
        {
          context.Result = new UnauthorizedResult();
        }
      }
    }

    private bool AuthorizationIsValid(string token)
    {
      return token.StartsWith("Bearer ", StringComparison.Ordinal) && token.Substring(7).IsValidAccessTokenWithScope(Scope);
    }

    private const string _authorizationHeaderName = "Authorization";
  }
}
