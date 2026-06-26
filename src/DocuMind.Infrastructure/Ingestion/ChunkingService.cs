using DocuMind.Core.Ingestion;

namespace DocuMind.Infrastructure.Ingestion;

/// <summary>
/// Character-based chunker that approximates token counts (~3.5 chars/token).
/// Targets ~800-token chunks with ~100-token overlap and prefers to break on
/// sentence boundaries so chunks stay semantically coherent. Chunking is done
/// per page so a chunk never spans pages, keeping page attribution exact.
/// </summary>
public sealed class ChunkingService : IChunkingService
{
    private const double CharsPerToken = 3.5;
    private const int TargetTokens = 800;
    private const int OverlapTokens = 100;

    // Window (in chars) to look back from the hard cut for a sentence boundary.
    private const int SentenceLookbackChars = 400;

    private static readonly int TargetChars = (int)(TargetTokens * CharsPerToken);   // ~2800
    private static readonly int OverlapChars = (int)(OverlapTokens * CharsPerToken); // ~350

    private static readonly char[] SentenceEnders = ['.', '!', '?', '\n'];

    public IReadOnlyList<ChunkDraft> Chunk(IReadOnlyList<ExtractedPage> pages)
    {
        var chunks = new List<ChunkDraft>();
        var index = 0;

        foreach (var page in pages)
        {
            var text = NormalizeWhitespace(page.Text);
            if (text.Length == 0)
            {
                continue;
            }

            foreach (var slice in SplitPage(text))
            {
                chunks.Add(new ChunkDraft(index++, slice, page.PageNumber));
            }
        }

        return chunks;
    }

    private static IEnumerable<string> SplitPage(string text)
    {
        var start = 0;

        while (start < text.Length)
        {
            var hardEnd = Math.Min(start + TargetChars, text.Length);
            var end = hardEnd;

            // If we're not at the end of the page, try to end on a sentence boundary.
            if (end < text.Length)
            {
                var boundary = FindSentenceBoundary(text, hardEnd);
                if (boundary > start)
                {
                    end = boundary;
                }
            }

            var chunk = text[start..end].Trim();
            if (chunk.Length > 0)
            {
                yield return chunk;
            }

            if (end >= text.Length)
            {
                yield break;
            }

            // Step forward, leaving an overlap. Guard against non-progress.
            var next = end - OverlapChars;
            start = next > start ? next : end;
        }
    }

    /// <summary>
    /// Searches backward from <paramref name="hardEnd"/> for the position just
    /// after a sentence ender, within the lookback window. Returns
    /// <paramref name="hardEnd"/> if none is found.
    /// </summary>
    private static int FindSentenceBoundary(string text, int hardEnd)
    {
        var lowerBound = Math.Max(0, hardEnd - SentenceLookbackChars);
        for (var i = hardEnd - 1; i >= lowerBound; i--)
        {
            if (Array.IndexOf(SentenceEnders, text[i]) >= 0)
            {
                return i + 1; // include the punctuation in this chunk
            }
        }

        return hardEnd;
    }

    /// <summary>Collapses runs of whitespace while keeping single spaces.</summary>
    private static string NormalizeWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWasSpace = false;
            }
        }

        return builder.ToString().Trim();
    }
}
