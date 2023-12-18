using System;
using System.Security.Cryptography;
using System.Text;

namespace Gradinware
{
  public static class TokenGenerator
  {
    public static string Generate(int length)
    {
      StringBuilder token = new StringBuilder();
      using (var rng = RandomNumberGenerator.Create())
      {
        byte[] buffer = new byte[sizeof(uint)];
        while (length-- > 0)
        {
          rng.GetBytes(buffer);
          uint num = BitConverter.ToUInt32(buffer, 0);
          token.Append(_validCharacters[(int)(num % (uint) _validCharacters.Length)]);
        }
      }

      return token.ToString();
    }

    private const string _validCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
  }
}
