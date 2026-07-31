using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CodeMeridian.Evolution.Api;

internal sealed class EvolutionExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "The request is invalid."),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "The requested ledger item was not found."),
            InvalidOperationException => (
                StatusCodes.Status409Conflict,
                "The operation conflicts with current governance or capability state."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "The request could not be completed.")
        };
        var detail = status == StatusCodes.Status500InternalServerError
            ? "An unexpected error occurred."
            : exception.Message;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}

