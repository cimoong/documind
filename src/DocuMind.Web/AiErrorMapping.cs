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

            // Rate limited even after retries.
            case ClientResultException { Status: 429 } cre:
                logger.LogWarning(cre, "Gemini rate limit reached");
                return Results.Problem(
                    title: "AI rate limit reached",
                    detail: "The AI service is rate limiting requests. Please wait a moment and try again.",
                    statusCode: StatusCodes.Status429TooManyRequests);

            case ClientResultException cre:
                logger.LogError(cre, "Gemini request failed with status {Status}", cre.Status);
                return Results.Problem(
                    title: "AI request failed",
                    detail: $"The AI service returned an error (HTTP {cre.Status}).",
                    statusCode: StatusCodes.Status502BadGateway);

            case HttpRequestException or TaskCanceledException or TimeoutException:
                logger.LogError(ex, "Could not reach Gemini");
                return Results.Problem(
                    title: "AI service unreachable",
                    detail: "Could not reach the AI service. Please check connectivity and try again.",
                    statusCode: StatusCodes.Status504GatewayTimeout);

            // Any other failure (e.g. database down): generic, no stack trace leak.
            default:
                logger.LogError(ex, "Unexpected error handling the request");
                return Results.Problem(
                    title: "Unexpected error",
                    detail: "An unexpected error occurred. Please try again.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
