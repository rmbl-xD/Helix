namespace Helix;

/// <summary>
/// Runs before the handler for a given request type. All registered pre-processors
/// execute in order before the pipeline behaviors and handler are invoked.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken = default);
}
