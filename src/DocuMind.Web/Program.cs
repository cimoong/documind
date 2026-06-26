using System.ClientModel;
using DocuMind.Core.Entities;
using DocuMind.Infrastructure;
using DocuMind.Web.Components;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// DocuMind data layer (EF Core + PostgreSQL/pgvector).
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Connectivity check: embeds "test" and reports the vector length (expected 768).
app.MapGet("/health/ai", async (
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("AiHealth");
    try
    {
        var embedding = await embeddingGenerator.GenerateVectorAsync("test", cancellationToken: cancellationToken);
        var length = embedding.Length;
        var expected = DocumentChunk.EmbeddingDimensions;

        return Results.Ok(new
        {
            status = length == expected ? "OK" : "DIMENSION_MISMATCH",
            model = "gemini-embedding-001",
            vectorLength = length,
            expected
        });
    }
    // Gemini returns 401/403 for auth, but also 400 with an "API key" message
    // when the key is missing/invalid — treat all of these as auth failures.
    catch (ClientResultException ex) when (
        ex.Status is 401 or 403 ||
        (ex.Status == 400 && ex.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)))
    {
        logger.LogError(ex, "Gemini authentication failed");
        return Results.Problem(
            title: "AI authentication failed",
            detail: "Gemini rejected the API key (HTTP " + ex.Status + "). Check that " +
                    "\"Gemini:ApiKey\" is set correctly via user-secrets.",
            statusCode: StatusCodes.Status401Unauthorized);
    }
    catch (ClientResultException ex)
    {
        logger.LogError(ex, "Gemini request failed with status {Status}", ex.Status);
        return Results.Problem(
            title: "AI request failed",
            detail: $"Gemini returned HTTP {ex.Status}: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        logger.LogError(ex, "Could not reach Gemini");
        return Results.Problem(
            title: "AI service unreachable",
            detail: "Could not reach the Gemini endpoint. Check network connectivity. " + ex.Message,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.Run();
