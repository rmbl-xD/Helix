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
    /// Publishes a notification to all registered handlers. Handlers execute sequentially.
    /// </summary>
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Creates an async stream by sending a streaming request to its handler.
    /// </summary>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}
