namespace Core.Features.Documents;

public sealed class StoreUnavailableException : Exception
{
    public StoreUnavailableException()
    {
    }

    public StoreUnavailableException(string message)
        : base(message)
    {
    }

    public StoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
