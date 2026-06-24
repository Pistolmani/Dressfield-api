using Dressfield.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dressfield.Tests.TestInfrastructure;

/// <summary>
/// SQLite in-memory DbContext factory. The connection must stay open for the database
/// to live, so callers own and dispose both. EnsureCreated (not Migrate) builds the
/// schema from the model directly - the checked-in migrations are MySQL-flavored.
/// </summary>
public static class TestDb
{
    public static (DressfieldDbContext Db, SqliteConnection Conn) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var db = new DressfieldDbContext(BuildOptions(conn));
        db.Database.EnsureCreated();
        return (db, conn);
    }

    public static DbContextOptions<DressfieldDbContext> BuildOptions(SqliteConnection conn) =>
        new DbContextOptionsBuilder<DressfieldDbContext>()
            .UseSqlite(conn)
            .Options;
}
