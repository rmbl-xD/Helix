namespace Helix;

/// <summary>
/// Runs after the handler for a given request type. All registered post-processors
/// execute in order after the pipeline behaviors and handler have completed.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestPostProcessor<in TRequest, in TResponse>
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken = default);
}
