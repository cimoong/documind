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

        // 3. Embed (batched).
        var embeddings = await EmbedAsync(drafts, cancellationToken);

        // 4. Persist.
        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = isPdf ? "application/pdf" : "text/plain",
            UploadedAtUtc = DateTime.UtcNow,
            TotalChunks = drafts.Count,
        };

        for (var i = 0; i < drafts.Count; i++)
        {
            document.Chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = drafts[i].ChunkIndex,
                Content = drafts[i].Content,
                PageNumber = drafts[i].PageNumber,
                Embedding = new Vector(embeddings[i]),
            });
        }

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Saved document {DocumentId} with {ChunkCount} chunk(s)",
            document.Id, document.TotalChunks);

        return new IngestionResult(document.Id, fileName, document.TotalChunks);
    }

    private async Task<List<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<ChunkDraft> drafts,
        CancellationToken cancellationToken)
    {
        var vectors = new List<ReadOnlyMemory<float>>(drafts.Count);
        if (drafts.Count == 0)
        {
            return vectors;
        }

        for (var offset = 0; offset < drafts.Count; offset += EmbeddingBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = drafts
                .Skip(offset)
                .Take(EmbeddingBatchSize)
                .Select(d => d.Content)
                .ToList();

            var generated = await embeddingGenerator.GenerateAsync(batch, cancellationToken: cancellationToken);
            vectors.AddRange(generated.Select(e => e.Vector));

            var done = Math.Min(offset + EmbeddingBatchSize, drafts.Count);
            logger.LogInformation("Embedded {Done}/{Total} chunk(s)", done, drafts.Count);

            if (done < drafts.Count)
            {
                await Task.Delay(DelayBetweenBatchesMs, cancellationToken);
            }
        }

        return vectors;
    }

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
