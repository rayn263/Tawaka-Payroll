namespace Tawaka.Application.Common;

/// <summary>A single validation failure, tied to the field that caused it.</summary>
public sealed record ValidationError(string Field, string Message)
{
    public override string ToString() => $"{Field}: {Message}";
}

/// <summary>
/// The outcome of validating a command. Failures are returned rather than thrown, because a form
/// needs every problem at once, not the first one.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationError> _errors = new();

    public IReadOnlyList<ValidationError> Errors => _errors;

    public bool IsValid => _errors.Count == 0;

    public static ValidationResult Success() => new();

    public ValidationResult Add(string field, string message)
    {
        _errors.Add(new ValidationError(field, message));
        return this;
    }

    public ValidationResult AddIf(bool condition, string field, string message)
    {
        if (condition)
        {
            Add(field, message);
        }

        return this;
    }

    public ValidationResult Require(string? value, string field, string? message = null) =>
        AddIf(string.IsNullOrWhiteSpace(value), field, message ?? $"{field} is required.");

    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new ValidationException(this);
        }
    }

    public override string ToString() => string.Join("; ", _errors);
}

public sealed class ValidationException : Exception
{
    public ValidationException(ValidationResult result)
        : base("Validation failed: " + result)
    {
        Result = result;
    }

    public ValidationResult Result { get; }
}

/// <summary>Result of an operation that can fail validation, without using exceptions for flow.</summary>
public sealed class OperationResult<T>
{
    private OperationResult(T? value, ValidationResult validation)
    {
        Value = value;
        Validation = validation;
    }

    public T? Value { get; }

    public ValidationResult Validation { get; }

    public bool Succeeded => Validation.IsValid;

    public static OperationResult<T> Success(T value) => new(value, ValidationResult.Success());

    public static OperationResult<T> Failed(ValidationResult validation) => new(default, validation);
}
