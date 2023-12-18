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
      return BCrypt.Net.BCrypt.Verify(password, otherPassword);
    }
  }
}
