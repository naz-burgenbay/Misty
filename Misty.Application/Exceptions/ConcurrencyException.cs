namespace Misty.Application.Exceptions;

public class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message) { }

    public ConcurrencyException(string entityName, object key)
        : base($"{entityName} '{key}' has been modified by another user.") { }
}
