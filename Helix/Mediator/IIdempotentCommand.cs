namespace Helix;

/// <summary>
/// Marker interface for a command that supports idempotent execution.
/// When an <see cref="IIdempotencyStore"/> is registered, duplicate commands
/// with the same <see cref="IdempotencyKey"/> return the previously stored response
/// without re-executing the handler.
/// </summary>
public interface IIdempotentCommand : ICommand
{
    /// <summary>
    /// A unique key that identifies this command instance for deduplication.
    /// </summary>
    Guid IdempotencyKey { get; }
}

/// <summary>
/// Marker interface for an idempotent command with a response.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IIdempotentCommand<out TResponse> : ICommand<TResponse>
{
    /// <summary>
    /// A unique key that identifies this command instance for deduplication.
    /// </summary>
    Guid IdempotencyKey { get; }
}

/// <summary>
/// Stores and checks idempotency keys to prevent duplicate command execution.
/// Implement this interface with your preferred storage mechanism (in-memory, database, Redis, etc.)
/// and register it in DI to enable automatic idempotency support.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Checks whether a command with the given idempotency key has already been processed.
    /// </summary>
    Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the previously stored response for a processed command.
    /// </summary>
    Task<TResponse?> GetResponseAsync<TResponse>(Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the response for a successfully processed command.
    /// </summary>
    Task SaveAsync<TResponse>(Guid idempotencyKey, TResponse response, CancellationToken cancellationToken = default);
}
