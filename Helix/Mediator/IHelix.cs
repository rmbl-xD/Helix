namespace Helix;

/// <summary>
/// Sends requests and notifications through the Helix pipeline.
/// </summary>
public interface IHelix
{
    /// <summary>
    /// Sends a request to its single handler, passing through pre-processors, pipeline behaviors,
    /// post-processors, and exception handling.
    /// </summary>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command with a response through the pipeline. Provides compile-time
    /// safety that the request is a write operation.
    /// </summary>
    Task<TResponse> SendCommand<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a command with no return value through the pipeline. Provides compile-time
    /// safety that the request is a write operation.
    /// </summary>
    Task SendCommand(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a query through the pipeline. Provides compile-time safety that
    /// the request is a read operation.
    /// </summary>
    Task<TResponse> SendQuery<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification to all registered handlers. Handlers execute sequentially.
    /// </summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Creates an async stream by sending a streaming request to its handler.
    /// </summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}
