using System.Diagnostics;
using System.Text;
using DocuMind.Core.Rag;
using DocuMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace DocuMind.Infrastructure.Rag;

/// <summary>
/// RAG pipeline: embed the question, retrieve the nearest chunks by cosine
/// distance (pgvector <c>&lt;=&gt;</c>), then ask Gemini to answer strictly from
/// the retrieved context with source markers for citations.
/// </summary>
public sealed class RagService(
    DocuMindDbContext db,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IChatClient chatClient,
    ILogger<RagService> logger) : IRagService
{
    private const int DefaultTopK = 5;
    private const int MaxTopK = 20;
    private const int SnippetLength = 200;

    // Firm grounding instructions to keep the model from hallucinating.
    private const string SystemPrompt =
        "Anda adalah asisten tanya-jawab dokumen. Jawab HANYA berdasarkan konteks " +
        "yang diberikan. Jika jawaban tidak ada di dalam konteks, katakan dengan " +
        "jujur bahwa informasi tidak ditemukan dalam dokumen — jangan mengarang. " +
        "Selalu sertakan rujukan ke sumber yang Anda gunakan, dengan format penanda " +
        "seperti [Sumber 1] sesuai konteks. Jawab dalam bahasa yang sama dengan pertanyaan.";

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken = default)
    {
        var question = request.Question?.Trim() ?? string.Empty;
        var topK = Math.Clamp(request.TopK ?? DefaultTopK, 1, MaxTopK);

        logger.LogInformation(
            "RAG question received (topK={TopK}, documentId={DocumentId}): {Question}",
            topK, request.DocumentId, question);

        // a. Embed the question.
        var questionVector = await embeddingGenerator.GenerateVectorAsync(question, cancellationToken: cancellationToken);
        var query = new Vector(questionVector);

        // b. Retrieve nearest chunks by cosine distance, optionally scoped to one document.
        var chunks = db.DocumentChunks.AsNoTracking();
        if (request.DocumentId is Guid documentId)
        {
            chunks = chunks.Where(c => c.DocumentId == documentId);
        }

        // Order by the cosine-distance expression directly (so EF translates it to
        // the pgvector `<=>` operator and uses the HNSW index), then project.
        var hits = await chunks
            .OrderBy(c => c.Embedding!.CosineDistance(query))
            .Take(topK)
            .Select(c => new RetrievedChunk(
                c.DocumentId,
                c.Document.FileName,
                c.ChunkIndex,
                c.PageNumber,
                c.Content,
                c.Embedding!.CosineDistance(query)))
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Retrieved {Count} chunk(s); top distance={TopDistance}",
            hits.Count, hits.Count > 0 ? hits[0].Distance : null);

        // 5. No relevant context (empty DB, scoped doc with no chunks, etc.).
        if (hits.Count == 0)
        {
            var message = request.DocumentId is null
                ? "Tidak ada dokumen yang terindeks. Silakan unggah dokumen terlebih dahulu."
                : "Tidak ada konteks yang ditemukan untuk dokumen tersebut.";
            return new AskResponse(message, [], 0);
        }

        // c. Augment: build context with explicit source markers.
        var context = BuildContext(hits);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, $"Konteks:\n{context}\nPertanyaan: {question}"),
        };

        // d. Generate.
        var stopwatch = Stopwatch.StartNew();
        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        logger.LogInformation(
            "Chat generation completed in {ElapsedMs} ms (model={Model})",
            stopwatch.ElapsedMilliseconds, "gemini-2.5-flash");
        var answer = response.Text;

        var citations = hits
            .Select(h => new Citation(h.DocumentId, h.FileName, h.ChunkIndex, h.PageNumber, Snippet(h.Content), h.Content))
            .ToList();

        logger.LogInformation("RAG answer generated using {Used} chunk(s)", citations.Count);
        return new AskResponse(answer, citations, citations.Count);
    }

    private static string BuildContext(IReadOnlyList<RetrievedChunk> hits)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < hits.Count; i++)
        {
            var h = hits[i];
            var marker = h.PageNumber is int page
                ? $"[Sumber {i + 1}: {h.FileName}, hal. {page}]"
                : $"[Sumber {i + 1}: {h.FileName}]";

            builder.AppendLine(marker);
            builder.AppendLine(h.Content);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Snippet(string content)
    {
        var trimmed = content.Trim();
        return trimmed.Length <= SnippetLength
            ? trimmed
            : trimmed[..SnippetLength] + "…";
    }

    /// <summary>Projection of a retrieved chunk plus its cosine distance.</summary>
    private sealed record RetrievedChunk(
        Guid DocumentId,
        string FileName,
        int ChunkIndex,
        int? PageNumber,
        string Content,
        double Distance);
}
