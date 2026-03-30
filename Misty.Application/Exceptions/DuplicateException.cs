namespace Misty.Application.Exceptions;

public class DuplicateException : Exception
{
    public DuplicateException(string message) : base(message) { }

    public DuplicateException(string entityName, string conflictField, object conflictValue)
        : base($"{entityName} with {conflictField} '{conflictValue}' already exists.") { }
}
