using System;

namespace Quilting.Exceptions
{
  public class InvalidTypeException : Exception
  {
    public InvalidTypeException()
      : base("Invalid type.")
    {
    }

    public InvalidTypeException(string propertyName)
      : base($"Invalid type for property: {propertyName}.")
    {
    }
  }
}
