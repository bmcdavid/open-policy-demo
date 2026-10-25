namespace Core.Features.Authorization;

public sealed class PolicyEngineUnavailableException : Exception
{
    public PolicyEngineUnavailableException()
    {
    }

    public PolicyEngineUnavailableException(string message) : base(message)
    {
    }

    public PolicyEngineUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
