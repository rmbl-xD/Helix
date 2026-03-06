namespace Helix;

/// <summary>
/// Performs a side-effect action when an exception occurs during request processing.
/// Always runs after exception handlers regardless of whether the exception was recovered.
/// Useful for logging, metrics, or alerting.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
public interface IRequestExceptionAction<in TRequest>
{
    Task Execute(TRequest request, Exception exception, CancellationToken cancellationToken = default);
}
