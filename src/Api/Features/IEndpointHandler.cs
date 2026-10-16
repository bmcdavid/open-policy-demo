namespace Api.Features;

internal interface IEndpointHandler<TIn, TOut>
{
    Task<TOut> HandleAsync(TIn input, HttpContext httpContext, CancellationToken cancellationToken);
}

internal readonly record struct Nil
{
    public static Nil Empty => default;
}
