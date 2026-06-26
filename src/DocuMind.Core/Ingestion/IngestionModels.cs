namespace DocuMind.Core.Ingestion;

/// <summary>
/// A unit of extracted text with its source page (null when the format has no
/// pages, e.g. plain text).
/// </summary>
public sealed record ExtractedPage(int? PageNumber, string Text);

/// <summary>
/// A draft chunk produced by the chunking service, before embedding/persistence.
/// </summary>
public sealed record ChunkDraft(int ChunkIndex, string Content, int? PageNumber);

/// <summary>Summary returned after ingesting a document.</summary>
public sealed record IngestionResult(Guid DocumentId, string FileName, int ChunkCount);
