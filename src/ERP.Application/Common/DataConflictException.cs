namespace ERP.Application.Common;

public sealed class DataConflictException : Exception
{
    public DataConflictException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
