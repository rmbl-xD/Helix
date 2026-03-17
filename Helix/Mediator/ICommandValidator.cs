namespace Helix;

/// <summary>
/// Represents a single validation failure for a command property.
/// </summary>
/// <param name="PropertyName">The name of the property that failed validation.</param>
/// <param name="ErrorMessage">A description of the validation failure.</param>
public record ValidationFailure(string PropertyName, string ErrorMessage);

/// <summary>
/// Contains the result of validating a command.
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// The list of validation failures. Empty if the command is valid.
    /// </summary>
    public List<ValidationFailure> Errors { get; } = [];

    /// <summary>
    /// Whether the command passed all validation rules.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Thrown when one or more command validation failures occur.
/// </summary>
public class ValidationException : Exception
{
    /// <summary>
    /// The validation failures that caused the exception.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> errors)
        : base("One or more validation failures occurred.")
    {
        Errors = errors.ToList().AsReadOnly();
    }
}

/// <summary>
/// Validates a command with a response before it is handled.
/// Multiple validators can be registered per command type; all are executed and
/// their failures are aggregated into a single <see cref="ValidationException"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommandValidator<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<ValidationResult> Validate(TCommand command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates a command with no return value before it is handled.
/// Multiple validators can be registered per command type; all are executed and
/// their failures are aggregated into a single <see cref="ValidationException"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
public interface ICommandValidator<in TCommand>
    where TCommand : ICommand
{
    Task<ValidationResult> Validate(TCommand command, CancellationToken cancellationToken = default);
}
