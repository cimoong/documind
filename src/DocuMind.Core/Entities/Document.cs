namespace DocuMind.Core.Entities;

/// <summary>
/// A source document uploaded to DocuMind. Its text is split into many
/// <see cref="DocumentChunk"/> rows that get embedded and searched.
/// </summary>
public class Document
{
    public Guid Id { get; set; }

    /// <summary>Original file name as uploaded (e.g. "handbook.pdf").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type of the upload (e.g. "application/pdf").</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Timestamp of upload, stored in UTC.</summary>
    public DateTime UploadedAtUtc { get; set; }

    /// <summary>Number of chunks produced from this document.</summary>
    public int TotalChunks { get; set; }

    /// <summary>Navigation: the chunks belonging to this document.</summary>
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}
