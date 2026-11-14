using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Gradinware
{
  public static class MiscellaneousExtensions
  {
    public static Dictionary<string, string[]> ToDictionary(this ModelStateDictionary modelState)
    {
      return modelState.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Errors.Select(e => e.ErrorMessage).ToArray());
    }

    public static double ToJsDate(this DateTime dateTime)
    {
      return dateTime
        .Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
        .TotalMilliseconds;
    }

  }
}
