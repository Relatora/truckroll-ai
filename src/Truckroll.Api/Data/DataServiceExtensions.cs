using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Truckroll.Data;

namespace Truckroll.Api.Data;

/// <summary>Registers EF Core and keeps the schema current in development.</summary>
public static class DataServiceExtensions
{
    /// <summary>
    /// Adds <see cref="TruckrollDbContext"/> on top of the <see cref="MySqlDataSource"/> that
    /// Aspire's MySQL integration registers, so EF queries share its connection pool, health
    /// check and tracing instead of opening a second, invisible pool.
    /// </summary>
    public static IHostApplicationBuilder AddTruckrollData(this IHostApplicationBuilder builder)
    {
        // The EF provider needs two connection options it would normally set itself. Given a
        // shared data source it can't change the connection, so it insists they're already there.
        builder.AddMySqlDataSource("truckroll", settings =>
        {
            if (settings.ConnectionString is { } connectionString)
            {
                settings.ConnectionString = new MySqlConnectionStringBuilder(connectionString)
                {
                    AllowUserVariables = true,
                    UseAffectedRows = false,
                }.ConnectionString;
            }
        });

        builder.Services.AddDbContext<TruckrollDbContext>((services, options) =>
            options.UseMySql(services.GetRequiredService<MySqlDataSource>(), TruckrollDbContext.ServerVersion));

        return builder;
    }

    /// <summary>
    /// Applies pending migrations at startup, in development and tests only. In production,
    /// migrations run as a separate, reviewed deployment step (an EF migrations bundle), never
    /// as a side effect of an app instance starting.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TruckrollDbContext>();
        await db.Database.MigrateAsync();
    }
}
