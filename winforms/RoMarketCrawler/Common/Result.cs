namespace RoMarketCrawler.Common;

/// <summary>
/// Result pattern for standardized error handling.
/// Represents either a success or failure outcome.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public Exception? Exception { get; }

    protected Result(bool isSuccess, string? error, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        Exception = exception;
    }

    /// <summary>
    /// Create a success result
    /// </summary>
    public static Result Success() => new(true, null);

    /// <summary>
    /// Create a failure result with error message
    /// </summary>
    public static Result Failure(string error) => new(false, error);

    /// <summary>
    /// Create a failure result from exception
    /// </summary>
    public static Result Failure(Exception ex) => new(false, ex.Message, ex);

    /// <summary>
    /// Create a typed success result
    /// </summary>
    public static Result<T> Success<T>(T value) => new(value, true, null);

    /// <summary>
    /// Create a typed failure result
    /// </summary>
    public static Result<T> Failure<T>(string error) => new(default!, false, error);

    /// <summary>
    /// Create a typed failure result from exception
    /// </summary>
    public static Result<T> Failure<T>(Exception ex) => new(default!, false, ex.Message, ex);
}

/// <summary>
/// Generic result pattern with typed value.
/// Represents either a success with value or failure.
/// </summary>
public class Result<T> : Result
{
    public T Value { get; }

    internal Result(T value, bool isSuccess, string? error, Exception? exception = null)
        : base(isSuccess, error, exception)
    {
        Value = value;
    }

    /// <summary>
    /// Get value or default if failure
    /// </summary>
    public T GetValueOrDefault(T defaultValue = default!)
    {
        return IsSuccess ? Value : defaultValue;
    }

    /// <summary>
    /// Match on success or failure
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(Value) : onFailure(Error ?? "Unknown error");
    }

    /// <summary>
    /// Execute action on success
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
            action(Value);
        return this;
    }

    /// <summary>
    /// Execute action on failure
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure)
            action(Error ?? "Unknown error");
        return this;
    }

    /// <summary>
    /// Map success value to new type
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        return IsSuccess
            ? Result.Success(mapper(Value))
            : Result.Failure<TNew>(Error ?? "Unknown error");
    }

    /// <summary>
    /// Bind to another result operation
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        return IsSuccess ? binder(Value) : Result.Failure<TNew>(Error ?? "Unknown error");
    }

    /// <summary>
    /// Implicit conversion from value to success result
    /// </summary>
    public static implicit operator Result<T>(T value) => Result.Success(value);
}

/// <summary>
/// Extension methods for Result pattern
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Convert Task<T> to Task<Result<T>> with exception handling
    /// </summary>
    public static async Task<Result<T>> ToResultAsync<T>(this Task<T> task)
    {
        try
        {
            var value = await task;
            return Result.Success(value);
        }
        catch (Exception ex)
        {
            return Result.Failure<T>(ex);
        }
    }

    /// <summary>
    /// Convert nullable to Result
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull = "Value is null")
        where T : class
    {
        return value != null
            ? Result.Success(value)
            : Result.Failure<T>(errorIfNull);
    }

    /// <summary>
    /// Convert nullable value type to Result
    /// </summary>
    public static Result<T> ToResult<T>(this T? value, string errorIfNull = "Value is null")
        where T : struct
    {
        return value.HasValue
            ? Result.Success(value.Value)
            : Result.Failure<T>(errorIfNull);
    }

    /// <summary>
    /// Combine multiple results - all must succeed
    /// </summary>
    public static Result Combine(params Result[] results)
    {
        var failures = results.Where(r => r.IsFailure).ToList();
        if (failures.Count == 0)
            return Result.Success();

        var errors = string.Join("; ", failures.Select(f => f.Error));
        return Result.Failure(errors);
    }

    /// <summary>
    /// Try execute with exception handling
    /// </summary>
    public static Result<T> Try<T>(Func<T> func)
    {
        try
        {
            return Result.Success(func());
        }
        catch (Exception ex)
        {
            return Result.Failure<T>(ex);
        }
    }

    /// <summary>
    /// Try execute async with exception handling
    /// </summary>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> func)
    {
        try
        {
            var result = await func();
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            return Result.Failure<T>(ex);
        }
    }

    /// <summary>
    /// Try execute action with exception handling
    /// </summary>
    public static Result Try(Action action)
    {
        try
        {
            action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex);
        }
    }
}
