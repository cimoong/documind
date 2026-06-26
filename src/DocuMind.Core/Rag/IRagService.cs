namespace DocuMind.Core.Rag;

/// <summary>
/// Retrieval-augmented generation: embeds the question, retrieves the most
/// similar chunks, and asks the chat model to answer strictly from that context.
/// </summary>
public interface IRagService
{
    Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken = default);
}
