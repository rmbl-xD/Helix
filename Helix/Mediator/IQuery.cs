namespace Helix;

/// <summary>
/// Marker interface for a query (read operation).
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IQuery<out TResponse> : IRequest<TResponse>;
