using DocuMind.Core.Ingestion;
using DocuMind.Core.Rag;
using DocuMind.Infrastructure.Ai;
using DocuMind.Infrastructure.Ingestion;
using DocuMind.Infrastructure.Persistence;
using DocuMind.Infrastructure.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocuMind.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Connection string name read from configuration.</summary>
    public const string ConnectionStringName = "DocuMindDb";

    /// <summary>
    /// Registers DocuMind infrastructure services (currently the EF Core
    /// context backed by PostgreSQL + pgvector). Called from the Web host.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found in configuration.");

        services.AddDbContext<DocuMindDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        // Gemini chat + embedding clients (via OpenAI-compatible endpoint).
        services.AddGeminiAi(configuration);

        // Document ingestion pipeline.
        services.AddScoped<IChunkingService, ChunkingService>();
        services.AddScoped<IIngestionService, IngestionService>();

        // Retrieval-augmented Q&A.
        services.AddScoped<IRagService, RagService>();

        return services;
    }
}
