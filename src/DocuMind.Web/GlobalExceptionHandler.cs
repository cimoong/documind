using System.ClientModel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocuMind.Web;

/// <summary>
/// Safety net for any unhandled exception: logs it and returns a clean
/// ProblemDetails response so stack traces never leak to the client. Known AI
/// failures are mapped to friendlier statuses/messages.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = Map(exception);

        logger.LogError(exception, "Unhandled exception ({Status}) for {Path}", status, httpContext.Request.Path);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        });
    }

    private static (int Status, string Title, string Detail) Map(Exception ex) => ex switch
    {
        ClientResultException cre when cre.Status is 401 or 403 ||
            (cre.Status == 400 && cre.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)) =>
            (StatusCodes.Status401Unauthorized, "AI authentication failed",
             "The AI service rejected the API key. Check that \"Gemini:ApiKey\" is configured."),

        ClientResultException cre when cre.Status == 429 =>
            (StatusCodes.Status429TooManyRequests, "AI rate limit reached",
             "The AI service is rate limiting requests. Please wait a moment and try again."),

        ClientResultException cre =>
            (StatusCodes.Status502BadGateway, "AI request failed",
             $"The AI service returned an error (HTTP {cre.Status})."),

        HttpRequestException or TaskCanceledException or TimeoutException =>
            (StatusCodes.Status504GatewayTimeout, "AI service unreachable",
             "Could not reach the AI service. Please check connectivity and try again."),

        _ => (StatusCodes.Status500InternalServerError, "Unexpected error",
              "An unexpected error occurred. Please try again."),
    };
}
