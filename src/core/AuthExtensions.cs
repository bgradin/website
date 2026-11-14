namespace Gradinware
{
  public static class AuthExtensions
  {
    public static string EncryptPassword(this string password)
    {
      return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public static bool MatchesPassword(this string password, string otherPassword)
    {
      if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(otherPassword))
      {
        return false;
      }

      if (otherPassword.IsPasswordHash())
      {
        try
        {
          return BCrypt.Net.BCrypt.Verify(password, otherPassword);
        }
        catch (BCrypt.Net.SaltParseException)
        {
          return false;
        }
      }

      // Permit a one-time upgrade of records written by the original plaintext flow.
      return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(password),
        System.Text.Encoding.UTF8.GetBytes(otherPassword));
    }

    public static bool IsPasswordHash(this string value)
    {
      return value != null && value.Length == 60 &&
        (value.StartsWith("$2a$", System.StringComparison.Ordinal) ||
         value.StartsWith("$2b$", System.StringComparison.Ordinal) ||
         value.StartsWith("$2y$", System.StringComparison.Ordinal));
    }
  }
}
