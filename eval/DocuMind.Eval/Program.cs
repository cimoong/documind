using System.Text.Json;
using DocuMind.Core.Rag;
using DocuMind.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Simple RAG evaluation harness. For each question it calls the real RAG
// pipeline and checks two things:
//   (a) retrieval accuracy — did the expected document appear in the citations?
//   (b) answer accuracy    — does the answer contain the expected keywords?
// Prerequisites: the database is up, Gemini:ApiKey is configured (shared
// user-secrets), and your documents have already been ingested.

var builder = Host.CreateApplicationBuilder(args);

// Load config from the exe directory so it works regardless of the caller's CWD,
// and reuse the Web project's user-secrets (Gemini:ApiKey) in any environment.
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);
builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);

// Keep console output focused on eval results (quiet resilience/HTTP noise).
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.Error);
builder.Logging.AddFilter("System.Net.Http", LogLevel.Error);

builder.Services.AddInfrastructure(builder.Configuration);

using var host = builder.Build();

var questionsPath = Path.Combine(AppContext.BaseDirectory, "questions.json");
if (!File.Exists(questionsPath))
{
    Console.Error.WriteLine($"questions.json not found at {questionsPath}");
    return 1;
}

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var questions = JsonSerializer.Deserialize<List<EvalQuestion>>(
    await File.ReadAllTextAsync(questionsPath), jsonOptions) ?? [];

if (questions.Count == 0)
{
    Console.Error.WriteLine("No questions to evaluate.");
    return 1;
}

using var scope = host.Services.CreateScope();
var rag = scope.ServiceProvider.GetRequiredService<IRagService>();

var answerOk = 0;
var retrievalOk = 0;
var retrievalTotal = 0; // only questions that specify an expected document

Console.WriteLine($"Running {questions.Count} evaluation question(s)...\n");

for (var i = 0; i < questions.Count; i++)
{
    var q = questions[i];
    var hasExpectedDoc = !string.IsNullOrWhiteSpace(q.ExpectedDocument);
    if (hasExpectedDoc)
    {
        retrievalTotal++;
    }

    try
    {
        var response = await rag.AskAsync(new AskRequest(q.Question));

        var retrieval = hasExpectedDoc
            && response.Citations.Any(c =>
                c.FileName.Contains(q.ExpectedDocument!, StringComparison.OrdinalIgnoreCase));

        var answer = q.ExpectedAnswerContains.Length == 0
            || q.ExpectedAnswerContains.All(k =>
                response.Answer.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (retrieval)
        {
            retrievalOk++;
        }

        if (answer)
        {
            answerOk++;
        }

        var rMark = !hasExpectedDoc ? "R—" : retrieval ? "R+" : "R-";
        var aMark = answer ? "A+" : "A-";
        Console.WriteLine($"[{i + 1,2}] {rMark} {aMark}  {q.Question}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{i + 1,2}] ERR     {q.Question}  ->  {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"Retrieval: {retrievalOk}/{retrievalTotal} (questions with an expected document)");
Console.WriteLine($"Answer:    {answerOk}/{questions.Count}");
return 0;

internal sealed class EvalQuestion
{
    public string Question { get; set; } = string.Empty;
    public string[] ExpectedAnswerContains { get; set; } = [];
    public string? ExpectedDocument { get; set; }
}
