using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    public static T CopySharedProperties<T>(this T target, object source)
    {
      Type sourceType = source.GetType();
      Type targetType = typeof(T);
      BindingFlags publicInstanceFlags = BindingFlags.Public | BindingFlags.Instance;
      foreach (var propertyInfo in sourceType.GetProperties(publicInstanceFlags))
      {
        PropertyInfo targetPropertyInfo = targetType.GetProperty(propertyInfo.Name, publicInstanceFlags);
        if (targetPropertyInfo != null && propertyInfo.PropertyType == targetPropertyInfo.PropertyType)
        {
          object sourceValue = propertyInfo.GetValue(source);
          if (sourceValue != null)
          {
            targetPropertyInfo.SetValue(target, sourceValue);
          }
        }
      }

      return target;
    }
  }
}
