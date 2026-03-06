namespace Helix;

/// <summary>
/// Holds the recovery state for an exception handler. Call <see cref="SetHandled"/>
/// to mark the exception as recovered and supply a replacement response.
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
public class RequestExceptionHandlerState<TResponse>
{
    /// <summary>
    /// The replacement response supplied by the exception handler.
    /// </summary>
    public TResponse? Response { get; private set; }

    /// <summary>
    /// Whether an exception handler has handled (recovered from) the exception.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// Marks the exception as handled and provides a replacement response.
    /// </summary>
    public void SetHandled(TResponse response)
    {
        Response = response;
        Handled = true;
    }
}

/// <summary>
/// Handles exceptions thrown during request processing. Can recover from the exception
/// by providing a replacement response via <see cref="RequestExceptionHandlerState{TResponse}"/>.
/// Multiple handlers are invoked in order; processing stops at the first that calls <c>SetHandled</c>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public interface IRequestExceptionHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task Handle(TRequest request, Exception exception, RequestExceptionHandlerState<TResponse> state, CancellationToken cancellationToken = default);
}
