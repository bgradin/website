using System;
using System.Security.Cryptography;
using System.Text;

namespace Gradinware
{
  public static class TokenGenerator
  {
    public static string Generate(int length)
    {
      if (length < 1 || length > 512)
      {
        throw new ArgumentOutOfRangeException(nameof(length));
      }

      var token = new StringBuilder(length);
      for (var i = 0; i < length; i++)
      {
        token.Append(_validCharacters[RandomNumberGenerator.GetInt32(_validCharacters.Length)]);
      }
      return token.ToString();
    }

    public static string Hash(string token)
    {
      return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private const string _validCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
  }
}
