namespace DocuMind.Core.Ingestion;

/// <summary>
/// Orchestrates the full ingestion pipeline for a single uploaded document:
/// text extraction, chunking, embedding, and persistence.
/// </summary>
public interface IIngestionService
{
    /// <summary>
    /// Ingests one document. <paramref name="content"/> is the raw file stream;
    /// <paramref name="contentType"/> is its MIME type. Supported types: PDF and
    /// plain text.
    /// </summary>
    Task<IngestionResult> IngestAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
