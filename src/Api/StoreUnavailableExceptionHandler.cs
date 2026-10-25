using Core.Features.Documents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Api;

/// <summary>A store that cannot be reached is a 503, never a 500 or a silent success.</summary>
internal sealed partial class StoreUnavailableExceptionHandler(ILogger<StoreUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is not StoreUnavailableException)
        {
            return false;
        }

        LogStoreUnavailable(exception);
        await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "data store unavailable")
            .ExecuteAsync(httpContext);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A backing store is unavailable; answering 503")]
    private partial void LogStoreUnavailable(Exception exception);
}
