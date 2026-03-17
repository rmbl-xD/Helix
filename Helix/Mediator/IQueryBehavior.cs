namespace Helix;

/// <summary>
/// Pipeline behavior that applies only to queries.
/// Implementations are automatically resolved as <see cref="IPipelineBehavior{TRequest, TResponse}"/>
/// but constrained to query types, enabling query-specific cross-cutting concerns
/// such as caching or read-replica routing.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IQueryBehavior<in TQuery, TResponse> : IPipelineBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
