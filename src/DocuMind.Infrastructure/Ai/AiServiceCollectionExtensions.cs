using System.ClientModel;
using DocuMind.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace DocuMind.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gemini-backed <see cref="IChatClient"/> and
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> services, reached via
    /// Gemini's OpenAI-compatible endpoint and wrapped with Microsoft.Extensions.AI
    /// middleware (logging).
    /// </summary>
    public static IServiceCollection AddGeminiAi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "Gemini:ApiKey is not configured. Set it with: " +
                "dotnet user-secrets set \"Gemini:ApiKey\" \"<your-key>\" " +
                "(run from src/DocuMind.Web).")
            .Validate(o => Uri.TryCreate(o.Endpoint, UriKind.Absolute, out _),
                "Gemini:Endpoint must be an absolute URI.")
            .ValidateOnStart();

        // One OpenAIClient pointed at Gemini's OpenAI-compatible base address,
        // authenticated with the Gemini API key.
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) };
            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), clientOptions);
        });

        services.AddChatClient(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var openAi = sp.GetRequiredService<OpenAIClient>();
            return openAi.GetChatClient(options.ChatModel).AsIChatClient();
        })
        .UseLogging();

        services.AddEmbeddingGenerator(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var openAi = sp.GetRequiredService<OpenAIClient>();
            return openAi.GetEmbeddingClient(options.EmbeddingModel).AsIEmbeddingGenerator();
        })
        // Default the output dimensionality to 768 so embeddings match the
        // vector(768) column unless a caller overrides it.
        .ConfigureOptions(o => o.Dimensions ??= DocumentChunk.EmbeddingDimensions)
        .UseLogging();

        return services;
    }
}
