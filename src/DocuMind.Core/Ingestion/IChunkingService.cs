namespace DocuMind.Core.Ingestion;

/// <summary>
/// Splits extracted page text into overlapping chunks suitable for embedding,
/// preserving page numbers and preferring sentence boundaries.
/// </summary>
public interface IChunkingService
{
    /// <summary>
    /// Produces ordered chunks across all pages. <see cref="ChunkDraft.ChunkIndex"/>
    /// is sequential across the whole document.
    /// </summary>
    IReadOnlyList<ChunkDraft> Chunk(IReadOnlyList<ExtractedPage> pages);
}
