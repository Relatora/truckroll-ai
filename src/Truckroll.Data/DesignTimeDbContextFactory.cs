using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Truckroll.Data;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API or reaching a
/// database. The server version is fixed (<see cref="TruckrollDbContext.ServerVersion"/>), so the
/// placeholder connection string is never opened.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TruckrollDbContext>
{
    public TruckrollDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TruckrollDbContext>()
            .UseMySql("Server=localhost;Database=truckroll", TruckrollDbContext.ServerVersion)
            .Options;

        return new TruckrollDbContext(options);
    }
}
