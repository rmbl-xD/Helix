namespace Helix;

/// <summary>
/// Defines a handler for a query.
/// </summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

/// <summary>
/// Base class for query handlers, providing a simpler Handle override with
/// a query-named parameter for readability.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public abstract class QueryHandler<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public abstract Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken = default);
}
