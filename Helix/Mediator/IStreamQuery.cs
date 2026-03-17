namespace Helix;

/// <summary>
/// Marker interface for a streaming query (read-side streaming).
/// Combines the streaming capability of <see cref="IStreamRequest{TResponse}"/> with
/// the read-operation semantics of the CQRS query side.
/// </summary>
/// <typeparam name="TResponse">The type of each streamed item.</typeparam>
public interface IStreamQuery<out TResponse> : IStreamRequest<TResponse>;

/// <summary>
/// Defines a handler for a streaming query that produces an async stream of results.
/// </summary>
/// <typeparam name="TQuery">The stream query type.</typeparam>
/// <typeparam name="TResponse">The type of each streamed item.</typeparam>
public interface IStreamQueryHandler<in TQuery, out TResponse> : IStreamRequestHandler<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>;
