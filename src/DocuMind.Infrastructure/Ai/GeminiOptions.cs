using DocuMind.Core.Entities;

namespace DocuMind.Infrastructure.Ai;

/// <summary>
/// Strongly-typed configuration for accessing Google Gemini through its
/// OpenAI-compatible endpoint. Bound from the "Gemini" configuration section.
/// The API key must come from configuration (user-secrets locally) — never
/// hard-coded or committed.
/// </summary>
public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Gemini API key. Set via `dotnet user-secrets set "Gemini:ApiKey" &lt;key&gt;`.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Chat/generation model. gemini-2.0-flash is retired — do not use it.</summary>
    public string ChatModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Embedding model.</summary>
    public string EmbeddingModel { get; set; } = "gemini-embedding-001";

    /// <summary>
    /// Output dimensionality for embeddings. Must match the database
    /// vector(<see cref="DocumentChunk.EmbeddingDimensions"/>) column (768).
    /// </summary>
    public int EmbeddingDimensions { get; set; } = DocumentChunk.EmbeddingDimensions;

    /// <summary>Gemini's OpenAI-compatible base address.</summary>
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
}
