namespace ERP.Application.Common;

public sealed class Result<T>
{
    internal Result(bool isSuccess, T? value, ApplicationError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public ApplicationError? Error { get; }

}

public static class Result
{
    public static Result<T> Success<T>(T value) => new(true, value, null);

    public static Result<T> Failure<T>(string code, string message)
    {
        return new Result<T>(false, default, new ApplicationError(code, message));
    }
}
