using System.ClientModel;
using System.ClientModel.Primitives;
using DocuMind.Core.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using Polly;

namespace DocuMind.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers Gemini-backed <see cref="IChatClient"/> and
    /// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> services, reached via
    /// Gemini's OpenAI-compatible endpoint and wrapped with Microsoft.Extensions.AI
    /// middleware (logging).
    /// </summary>
    /// <summary>Named HttpClient used as the OpenAI transport to reach Gemini.</summary>
    public const string HttpClientName = "gemini";

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

        // Resilient HttpClient for all Gemini traffic: standard retry with
        // exponential backoff + jitter (handles HTTP 429 and 5xx/transient
        // failures), per-attempt and total timeouts, and a circuit breaker.
        services.AddHttpClient(HttpClientName)
            // The OpenAIClient is a singleton that captures one HttpClient for the
            // app's lifetime, so the factory must NOT rotate/dispose its handler
            // (the default 2-minute rotation disposes the resilience pipeline and
            // breaks long-running uploads). Pin the handler for the app lifetime.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .AddStandardResilienceHandler(o =>
            {
                // LLM calls can be slow; give each attempt room, and bound the total.
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(180);
                // Circuit-breaker sampling must be >= 2x the attempt timeout.
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);

                o.Retry.MaxRetryAttempts = 3;
                o.Retry.BackoffType = DelayBackoffType.Exponential;
                o.Retry.UseJitter = true;
                o.Retry.Delay = TimeSpan.FromSeconds(2);
            });

        // One OpenAIClient pointed at Gemini's OpenAI-compatible base address,
        // authenticated with the Gemini API key, sending through the resilient
        // HttpClient. The SDK's own retry is disabled so the resilience handler
        // owns retries (avoids compounding backoff).
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint),
                Transport = new HttpClientPipelineTransport(httpClient),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            };
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
