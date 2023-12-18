using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
    }

    private bool AuthorizationIsValid(string token)
    {
      return token.StartsWith("Bearer ", StringComparison.Ordinal) && token.Substring(7).IsValidAccessTokenWithScope(Scope);
    }

    private const string _authorizationHeaderName = "Authorization";
  }
}
