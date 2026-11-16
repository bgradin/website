using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gradinware.Data;

// Separate database: existing account migrations remain independent of sheet schemas.
public sealed class DndContext : SqliteDbContext
{
    public DbSet<DndDocument> Documents { get; set; }
    // EnsureCreated alone cannot upgrade the original one-sheet-per-user table.
    public void EnsureSchema()
    {
        Database.EnsureCreated();
        Database.OpenConnection();
        try
        {
            using var transaction = Database.BeginTransaction();
            using var command = Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Documents') WHERE name = 'Id'";
            if (Convert.ToInt32(command.ExecuteScalar()) == 0)
            {
                Database.ExecuteSqlRaw("""
                    CREATE TABLE "Documents_MultiSheet" (
                        "Id" TEXT NOT NULL CONSTRAINT "PK_Documents" PRIMARY KEY,
                        "UserId" INTEGER NOT NULL,
                        "SchemaVersion" INTEGER NOT NULL,
                        "Revision" INTEGER NOT NULL,
                        "Data" TEXT NOT NULL
                    );
                    INSERT INTO "Documents_MultiSheet" ("Id", "UserId", "SchemaVersion", "Revision", "Data")
                        SELECT lower(hex(randomblob(16))), "UserId", "SchemaVersion", "Revision", "Data" FROM "Documents";
                    DROP TABLE "Documents";
                    ALTER TABLE "Documents_MultiSheet" RENAME TO "Documents";
                    CREATE INDEX "IX_Documents_UserId" ON "Documents" ("UserId");
                    """);
            }
            transaction.Commit();
        }
        finally { Database.CloseConnection(); }
    }

    protected override string GetDatabaseName() => "dnd.db";
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DndDocument>().HasKey(d => d.Id);
        builder.Entity<DndDocument>().HasIndex(d => d.UserId);
        builder.Entity<DndDocument>().Property(d => d.Revision).IsConcurrencyToken();
    }
}

public sealed class DndDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    public int SchemaVersion { get; set; }
    public long Revision { get; set; }
    public string Data { get; set; } = "{}";
}
