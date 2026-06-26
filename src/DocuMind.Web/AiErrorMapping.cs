using System.ClientModel;

namespace DocuMind.Web;

/// <summary>
/// Maps exceptions from the AI provider (Gemini via the OpenAI client) to clean
/// HTTP problem responses, so callers get clear messages instead of 500s.
/// </summary>
public static class AiErrorMapping
{
    public static IResult ToProblem(Exception ex, ILogger logger)
    {
        switch (ex)
        {
            // Gemini returns 401/403 for auth, and 400 with an "API key" message
            // when the key is missing/invalid — treat all of these as auth errors.
            case ClientResultException cre when
                cre.Status is 401 or 403 ||
                (cre.Status == 400 && cre.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)):
                logger.LogError(cre, "Gemini authentication failed");
                return Results.Problem(
                    title: "AI authentication failed",
                    detail: $"Gemini rejected the API key (HTTP {cre.Status}). Check that " +
                            "\"Gemini:ApiKey\" is set correctly via user-secrets.",
                    statusCode: StatusCodes.Status401Unauthorized);

            case ClientResultException cre:
                logger.LogError(cre, "Gemini request failed with status {Status}", cre.Status);
                return Results.Problem(
                    title: "AI request failed",
                    detail: $"Gemini returned HTTP {cre.Status}: {cre.Message}",
                    statusCode: StatusCodes.Status502BadGateway);

            case HttpRequestException or TaskCanceledException:
                logger.LogError(ex, "Could not reach Gemini");
                return Results.Problem(
                    title: "AI service unreachable",
                    detail: "Could not reach the Gemini endpoint. Check network connectivity. " + ex.Message,
                    statusCode: StatusCodes.Status504GatewayTimeout);

            default:
                throw ex;
        }
    }
}
