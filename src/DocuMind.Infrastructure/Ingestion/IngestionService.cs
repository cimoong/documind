using System.ClientModel;
using System.Diagnostics;
using DocuMind.Core.Entities;
using DocuMind.Core.Ingestion;
using DocuMind.Infrastructure.Persistence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Pgvector;
using UglyToad.PdfPig;

namespace DocuMind.Infrastructure.Ingestion;

/// <summary>
/// End-to-end ingestion: extract text (PDF via PdfPig, plain text directly),
/// chunk it, embed each chunk with Gemini, and persist the document and chunks.
/// </summary>
public sealed class IngestionService(
    DocuMindDbContext db,
    IChunkingService chunkingService,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<IngestionService> logger) : IIngestionService
{
    // Embed in small batches with a short pause to respect free-tier rate limits.
    private const int EmbeddingBatchSize = 16;
    private const int DelayBetweenBatchesMs = 250;

    public async Task<IngestionResult> IngestAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // Buffer the upload so we can read length and seek (PdfPig needs a seekable source).
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var isPdf = IsPdf(fileName, contentType);
        logger.LogInformation(
            "Ingestion started for {FileName} ({Bytes} bytes, type={Type})",
            fileName, bytes.Length, isPdf ? "pdf" : "text");

        // 1. Extract.
        var pages = isPdf ? ExtractPdf(bytes) : ExtractText(bytes);
        var totalChars = pages.Sum(p => p.Text.Length);
        logger.LogInformation(
            "Extracted {Pages} page(s), {Chars} characters from {FileName}",
            pages.Count, totalChars, fileName);

        // 2. Chunk.
        var drafts = chunkingService.Chunk(pages);
        logger.LogInformation("Produced {ChunkCount} chunk(s) for {FileName}", drafts.Count, fileName);

        // 3. Embed (batched, tolerant of per-chunk failures).
        var embedded = await EmbedAsync(drafts, cancellationToken);

        // If nothing could be embedded at all (e.g. auth/connectivity), surface
        // the error instead of saving an empty document.
        if (drafts.Count > 0 && embedded.Successes.Count == 0)
        {
            throw embedded.LastError ?? new InvalidOperationException("Embedding failed for all chunks.");
        }

        // 4. Persist (only the chunks that were successfully embedded).
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = isPdf ? "application/pdf" : "text/plain",
            UploadedAtUtc = DateTime.UtcNow,
            TotalChunks = embedded.Successes.Count,
        };

        foreach (var (draft, vector) in embedded.Successes)
        {
            document.Chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = draft.ChunkIndex,
                Content = draft.Content,
                PageNumber = draft.PageNumber,
                Embedding = new Vector(vector),
            });
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        if (embedded.Skipped > 0)
        {
            logger.LogWarning(
                "Saved document {DocumentId} with partial success: {Saved} chunk(s) stored, {Skipped} skipped",
                document.Id, embedded.Successes.Count, embedded.Skipped);
        }
        else
        {
            logger.LogInformation(
                "Saved document {DocumentId} with {ChunkCount} chunk(s)",
                document.Id, document.TotalChunks);
        }

        return new IngestionResult(document.Id, fileName, embedded.Successes.Count, embedded.Skipped);
    }

    private async Task<EmbedOutcome> EmbedAsync(
        IReadOnlyList<ChunkDraft> drafts,
        CancellationToken cancellationToken)
    {
        var successes = new List<(ChunkDraft Draft, ReadOnlyMemory<float> Vector)>(drafts.Count);
        var skipped = 0;
        Exception? lastError = null;

        for (var offset = 0; offset < drafts.Count; offset += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchDrafts = drafts.Skip(offset).Take(EmbeddingBatchSize).ToList();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var generated = await embeddingGenerator.GenerateAsync(
                    batchDrafts.Select(d => d.Content), cancellationToken: cancellationToken);
                var vectors = generated.Select(e => e.Vector).ToList();

                for (var i = 0; i < batchDrafts.Count && i < vectors.Count; i++)
                {
                    successes.Add((batchDrafts[i], vectors[i]));
                }

                logger.LogInformation(
                    "Embedded batch of {Count} chunk(s) in {ElapsedMs} ms ({Done}/{Total})",
                    batchDrafts.Count, stopwatch.ElapsedMilliseconds, successes.Count + skipped, drafts.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;

                // Auth errors affect every call — abort rather than hammer the API.
                if (IsAuthError(ex))
                {
                    logger.LogError(ex, "Embedding failed due to authentication; aborting ingestion");
                    throw;
                }

                logger.LogWarning(ex,
                    "Batch embedding failed after retries; falling back to per-chunk embedding");

                foreach (var draft in batchDrafts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var vector = await embeddingGenerator.GenerateVectorAsync(
                            draft.Content, cancellationToken: cancellationToken);
                        successes.Add((draft, vector));
                    }
                    catch (Exception itemEx) when (itemEx is not OperationCanceledException)
                    {
                        skipped++;
                        lastError = itemEx;
                        logger.LogWarning(itemEx, "Skipping chunk #{Index} after embedding failure", draft.ChunkIndex);
                    }
                }
            }

            if (offset + EmbeddingBatchSize < drafts.Count)
            {
                await Task.Delay(DelayBetweenBatchesMs, cancellationToken);
            }
        }

        return new EmbedOutcome(successes, skipped, lastError);
    }

    private static bool IsAuthError(Exception ex) =>
        ex is ClientResultException { Status: 401 or 403 }
        || (ex is ClientResultException c && c.Status == 400
            && c.Message.Contains("API key", StringComparison.OrdinalIgnoreCase));

    private sealed record EmbedOutcome(
        IReadOnlyList<(ChunkDraft Draft, ReadOnlyMemory<float> Vector)> Successes,
        int Skipped,
        Exception? LastError);

    private static List<ExtractedPage> ExtractPdf(byte[] bytes)
    {
        var pages = new List<ExtractedPage>();
        using var pdf = PdfDocument.Open(bytes);
        foreach (var page in pdf.GetPages())
        {
            // Join detected words with spaces — more reliable word separation than
            // the raw concatenated letter stream (page.Text).
            var text = string.Join(' ', page.GetWords().Select(w => w.Text));
            pages.Add(new ExtractedPage(page.Number, text));
        }

        return pages;
    }

    private static List<ExtractedPage> ExtractText(byte[] bytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        return [new ExtractedPage(null, text)];
    }

    private static bool IsPdf(string fileName, string contentType)
    {
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
