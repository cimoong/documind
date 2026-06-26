using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocuMind.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the `dotnet ef` tools (migrations add / database
/// update). It lets EF build the context without booting the Web host. Uses the
/// local Docker connection string by default; override with the
/// DOCUMIND_DB_CONNECTION environment variable. This is local-only and contains
/// no secret beyond the development password.
/// </summary>
public class DocuMindDbContextFactory : IDesignTimeDbContextFactory<DocuMindDbContext>
{
    private const string LocalConnectionString =
        "Host=localhost;Port=5433;Database=documind;Username=postgres;Password=postgres";

    public DocuMindDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DOCUMIND_DB_CONNECTION")
            ?? LocalConnectionString;

        var options = new DbContextOptionsBuilder<DocuMindDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;

        return new DocuMindDbContext(options);
    }
}
