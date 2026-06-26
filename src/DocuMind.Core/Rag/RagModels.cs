namespace DocuMind.Core.Rag;

/// <summary>A question to answer over the indexed documents.</summary>
/// <param name="Question">The natural-language question.</param>
/// <param name="TopK">How many chunks to retrieve (optional, default 5).</param>
/// <param name="DocumentId">Restrict retrieval to a single document (optional).</param>
public sealed record AskRequest(string Question, int? TopK = null, Guid? DocumentId = null);

/// <summary>A source chunk that was supplied as context for the answer.</summary>
/// <param name="Snippet">Short preview for compact display.</param>
/// <param name="Context">Full chunk text, e.g. for an expandable "read more" view.</param>
public sealed record Citation(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    int? PageNumber,
    string Snippet,
    string Context);

/// <summary>The grounded answer plus its citations.</summary>
public sealed record AskResponse(
    string Answer,
    IReadOnlyList<Citation> Citations,
    int UsedChunks);
