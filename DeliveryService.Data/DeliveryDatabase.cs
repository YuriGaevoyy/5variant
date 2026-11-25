using System.Data.Common;
using DeliveryService.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DeliveryService.Data;

public static class DeliveryDatabase
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeliveryService", "delivery.db");

    public static void Configure(DbContextOptionsBuilder options, string path)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            ForeignKeys = true,
            DefaultTimeout = 10
        }.ToString();
        options.UseSqlite(connectionString).AddInterceptors(new SearchFunctions());
    }

    public static async Task Initialize(DeliveryDb db, string path, bool seedDemo, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await db.Database.MigrateAsync(ct);
        if (seedDemo)
            await DeliveryDemo.Seed(db, ct);
    }

    [DbFunction("delivery_contains", IsBuiltIn = true)]
    public static bool DeliveryContains(string value, string uppercase) =>
        throw new InvalidOperationException("Функция выполняется в SQLite.");

    private sealed class SearchFunctions : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Register(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            Register(connection);
            return Task.CompletedTask;
        }

        private static void Register(DbConnection connection) =>
            ((SqliteConnection)connection).CreateFunction<string, string, bool>("delivery_contains",
                (value, term) => value.Contains(term, StringComparison.OrdinalIgnoreCase), isDeterministic: true);
    }
}

public sealed class DeliveryDbFactory : IDesignTimeDbContextFactory<DeliveryDb>
{
    public DeliveryDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DeliveryDb>();
        DeliveryDatabase.Configure(options, DeliveryDatabase.DefaultPath);
        return new DeliveryDb(options.Options);
    }
}
