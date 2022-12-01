namespace Gradinware.Data
{
  internal sealed class ContentTrunk : SqliteBsonTrunk, IContentTrunk
  {
    public ContentTrunk()
      : base("content.db")
    {
    }

    public void EnsureCreated()
    {
      Database.EnsureCreated();
    }
  }
}
