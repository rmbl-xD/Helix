namespace Helix;

/// <summary>
/// Marker interface for a request with a response.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequest<out TResponse>;

/// <summary>
/// Marker interface for a request that returns no value.
/// </summary>
public interface IRequest : IRequest<Unit>;
