namespace ProductionLinePlanner.Application.Common;

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Success() => new Result(true, null);

    public static Result Failure(Error error) => new Result(false, error ?? throw new ArgumentNullException(nameof(error)));
}

public class Result<T> : Result
{
    protected Result(T? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new Result<T>(value, true, null);

    public static new Result<T> Failure(Error error) => new Result<T>(default, false, error);
}
