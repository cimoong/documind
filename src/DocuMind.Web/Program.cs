using System.ClientModel;
using DocuMind.Core.Entities;
using DocuMind.Core.Ingestion;
using DocuMind.Core.Rag;
using DocuMind.Infrastructure;
using DocuMind.Web;
using DocuMind.Web.Components;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// DocuMind data layer (EF Core + PostgreSQL/pgvector).
builder.Services.AddInfrastructure(builder.Configuration);

// Swagger / OpenAPI for trying the API endpoints.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
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
    catch (Exception ex) when (ex is ClientResultException or HttpRequestException or TaskCanceledException)
    {
        return AiErrorMapping.ToProblem(ex, logger);
    }
});

// Upload a document (PDF or .txt) and ingest it: extract -> chunk -> embed -> store.
const long maxUploadBytes = 20 * 1024 * 1024; // 20 MB
app.MapPost("/api/documents", async (
    IFormFile? file,
    IIngestionService ingestionService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("DocumentUpload");

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest(new { error = "No file was uploaded." });
    }

    if (file.Length > maxUploadBytes)
    {
        return Results.BadRequest(new
        {
            error = $"File exceeds the {maxUploadBytes / (1024 * 1024)} MB limit.",
            sizeBytes = file.Length
        });
    }

    var fileName = file.FileName;
    var contentType = file.ContentType ?? string.Empty;
    var isPdf = contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    var isText = contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase)
                 || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    if (!isPdf && !isText)
    {
        return Results.BadRequest(new
        {
            error = "Unsupported file type. Only PDF (.pdf) and plain text (.txt) are supported.",
            received = string.IsNullOrWhiteSpace(contentType) ? fileName : contentType
        });
    }

    try
    {
        await using var stream = file.OpenReadStream();
        var result = await ingestionService.IngestAsync(stream, fileName, contentType, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is ClientResultException or HttpRequestException or TaskCanceledException)
    {
        return AiErrorMapping.ToProblem(ex, logger);
    }
})
.DisableAntiforgery() // API endpoint: not a browser form post.
.WithName("UploadDocument")
.WithTags("Documents");

// Ask a question, answered strictly from retrieved document chunks, with citations.
app.MapPost("/api/ask", async (
    AskRequest request,
    IRagService ragService,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "The 'question' field is required." });
    }

    var logger = loggerFactory.CreateLogger("Ask");
    try
    {
        var response = await ragService.AskAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex) when (ex is ClientResultException or HttpRequestException or TaskCanceledException)
    {
        return AiErrorMapping.ToProblem(ex, logger);
    }
})
.WithName("Ask")
.WithTags("RAG");

app.Run();
