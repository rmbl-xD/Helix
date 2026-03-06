namespace Helix;

/// <summary>
/// Marker interface for a command that returns no value (write operation).
/// </summary>
public interface ICommand : IRequest<Unit>;

/// <summary>
/// Marker interface for a command with a response (write operation).
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface ICommand<out TResponse> : IRequest<TResponse>;
