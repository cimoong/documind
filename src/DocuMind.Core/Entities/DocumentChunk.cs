using Pgvector;

namespace DocuMind.Core.Entities;

/// <summary>
/// A contiguous slice of a <see cref="Document"/> together with its embedding
/// vector. This is the unit retrieved during similarity search for RAG.
/// </summary>
public class DocumentChunk
{
    /// <summary>
    /// Dimensionality of the embedding vector. Tied to the embedding model:
    /// gemini-embedding-001 with output dimensionality 768. Change here in one
    /// place if the model or configured dimension changes.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="Document"/>.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Zero-based position of this chunk within the document.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>The chunk text that gets embedded and shown as a citation.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Source page number, when known (null for formats without pages).</summary>
    public int? PageNumber { get; set; }

    /// <summary>
    /// The embedding for <see cref="Content"/>, stored as a pgvector
    /// vector(<see cref="EmbeddingDimensions"/>) column.
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>Navigation back to the owning document.</summary>
    public Document Document { get; set; } = null!;
}
