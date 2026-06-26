namespace DocuMind.Core.Documents;

/// <summary>Lightweight summary of an ingested document for listing in the UI.</summary>
public sealed record DocumentSummary(
    Guid Id,
    string FileName,
    DateTime UploadedAtUtc,
    int TotalChunks);

/// <summary>Read-only queries over ingested documents.</summary>
public interface IDocumentReadService
{
    Task<IReadOnlyList<DocumentSummary>> GetDocumentsAsync(CancellationToken cancellationToken = default);
}
