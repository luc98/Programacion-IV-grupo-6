namespace AvailabilitySite.Application.Common;

public sealed class Result<T>
{
    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(ApplicationError error)
    {
        IsSuccess = false;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public ApplicationError? Error { get; }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(ApplicationError error) => new(error);
}
