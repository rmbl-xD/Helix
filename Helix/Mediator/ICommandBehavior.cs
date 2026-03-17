namespace Helix;

/// <summary>
/// Pipeline behavior that applies only to commands with a response.
/// Implementations are automatically resolved as <see cref="IPipelineBehavior{TRequest, TResponse}"/>
/// but constrained to command types, enabling command-specific cross-cutting concerns
/// such as transaction wrapping or audit logging.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommandBehavior<in TCommand, TResponse> : IPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;
