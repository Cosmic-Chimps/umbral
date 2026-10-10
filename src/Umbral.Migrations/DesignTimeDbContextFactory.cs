using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Umbral.Migrations;

/// <summary>
/// Lets <c>dotnet ef</c> build a context without starting the application.
/// </summary>
/// <remarks>
/// Umbral's real startup refuses to run without UMBRAL__PUBLIC_ORIGIN and a
/// writable data directory, which is correct for a deployment and useless at
/// design time. This factory sidesteps it: migrations only need the model, and
/// the connection string here is never opened.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<UmbralContext>
{
    public UmbralContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<UmbralContext>()
            .UseSqlite("Data Source=design-time-only.db", b => b.MigrationsAssembly("Umbral.Migrations"))
            .UseOpenIddict()
            .Options;

        return new UmbralContext(options);
    }
}
