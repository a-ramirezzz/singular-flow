using Microsoft.AspNetCore.Diagnostics;

namespace SingularFlow.Api.ErrorHandling;

public sealed class InvalidSimulationRequestExceptionHandler :
    IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ArgumentOutOfRangeException)
        {
            return false;
        }

        IResult problemResult = Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid simulation request.",
            detail: exception.Message,
            instance: httpContext.Request.Path);

        await problemResult.ExecuteAsync(httpContext);

        return true;
    }
}