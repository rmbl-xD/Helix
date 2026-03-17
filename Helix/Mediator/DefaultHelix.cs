using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Helix;

/// <summary>
/// Default Helix implementation that resolves handlers, behaviors, processors,
/// and exception handlers from DI.
/// </summary>
public sealed class DefaultHelix(IServiceProvider serviceProvider) : IHelix
{
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();

        try
        {
            // 0. Idempotency check
            Guid? idempotencyKey = null;
            var idempotencyStore = serviceProvider.GetService<IIdempotencyStore>();

            if (idempotencyStore is not null)
            {
                if (request is IIdempotentCommand idempotentCmd)
                {
                    idempotencyKey = idempotentCmd.IdempotencyKey;
                }
                else
                {
                    var idempotentInterface = requestType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIdempotentCommand<>));
                    if (idempotentInterface is not null)
                    {
                        idempotencyKey = (Guid)idempotentInterface.GetProperty("IdempotencyKey")!.GetValue(request)!;
                    }
                }

                if (idempotencyKey.HasValue && await idempotencyStore.ExistsAsync(idempotencyKey.Value, cancellationToken))
                {
                    return (await idempotencyStore.GetResponseAsync<TResponse>(idempotencyKey.Value, cancellationToken))!;
                }
            }

            // 1. Pre-processors
            var preProcessorType = typeof(IRequestPreProcessor<>).MakeGenericType(requestType);
            foreach (var preProcessor in serviceProvider.GetServices(preProcessorType))
            {
                await (Task)preProcessorType.GetMethod("Process")!
                    .Invoke(preProcessor, [request, cancellationToken])!;
            }

            // 1b. Command validators
            var failures = new List<ValidationFailure>();

            if (request is ICommand)
            {
                var validatorType = typeof(ICommandValidator<>).MakeGenericType(requestType);
                foreach (var validator in serviceProvider.GetServices(validatorType))
                {
                    var result = await (Task<ValidationResult>)validatorType.GetMethod("Validate")!
                        .Invoke(validator, [request, cancellationToken])!;
                    failures.AddRange(result.Errors);
                }
            }

            if (requestType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            {
                var validatorWithResponseType = typeof(ICommandValidator<,>).MakeGenericType(requestType, typeof(TResponse));
                foreach (var validator in serviceProvider.GetServices(validatorWithResponseType))
                {
                    var result = await (Task<ValidationResult>)validatorWithResponseType.GetMethod("Validate")!
                        .Invoke(validator, [request, cancellationToken])!;
                    failures.AddRange(result.Errors);
                }
            }

            if (failures.Count > 0)
                throw new ValidationException(failures);

            // 2. Resolve handler
            var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            var handler = serviceProvider.GetService(handlerType)
                ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}.");

            // 3. Build behavior pipeline around the handler
            var behaviors = serviceProvider
                .GetServices(typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse)))
                .Cast<object>()
                .Reverse()
                .ToList();

            var handlerHandleMethod = handlerType.GetMethod("Handle")!;
            RequestHandlerDelegate<TResponse> pipeline = () =>
                (Task<TResponse>)handlerHandleMethod.Invoke(handler, [request, cancellationToken])!;

            if (behaviors.Count > 0)
            {
                var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
                var behaviorHandleMethod = behaviorType.GetMethod("Handle")!;

                foreach (var behavior in behaviors)
                {
                    var current = pipeline;
                    pipeline = () => (Task<TResponse>)behaviorHandleMethod.Invoke(behavior, [request, current, cancellationToken])!;
                }
            }

            var response = await pipeline();

            // 4. Post-processors
            var postProcessorType = typeof(IRequestPostProcessor<,>).MakeGenericType(requestType, typeof(TResponse));
            foreach (var postProcessor in serviceProvider.GetServices(postProcessorType))
            {
                await (Task)postProcessorType.GetMethod("Process")!
                    .Invoke(postProcessor, [request, response, cancellationToken])!;
            }

            // 4b. Save idempotency
            if (idempotencyKey.HasValue && idempotencyStore is not null)
            {
                await idempotencyStore.SaveAsync(idempotencyKey.Value, response, cancellationToken);
            }

            return response;
        }
        catch (Exception ex)
        {
            // 5. Exception handlers — can recover by supplying a replacement response
            var exHandlerType = typeof(IRequestExceptionHandler<,>).MakeGenericType(requestType, typeof(TResponse));
            var state = new RequestExceptionHandlerState<TResponse>();

            foreach (var exHandler in serviceProvider.GetServices(exHandlerType))
            {
                await (Task)exHandlerType.GetMethod("Handle")!
                    .Invoke(exHandler, [request, ex, state, cancellationToken])!;

                if (state.Handled)
                    break;
            }

            // 6. Exception actions — side effects (logging, metrics) that always run
            var exActionType = typeof(IRequestExceptionAction<>).MakeGenericType(requestType);
            foreach (var exAction in serviceProvider.GetServices(exActionType))
            {
                await (Task)exActionType.GetMethod("Execute")!
                    .Invoke(exAction, [request, ex, cancellationToken])!;
            }

            if (state.Handled)
                return state.Response!;

            throw;
        }
    }

    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var notificationType = notification.GetType();
        var handlerType = typeof(INotificationHandler<>).MakeGenericType(notificationType);
        var handleMethod = handlerType.GetMethod("Handle")!;

        foreach (var handler in serviceProvider.GetServices(handlerType))
        {
            await (Task)handleMethod.Invoke(handler, [notification, cancellationToken])!;
        }
    }

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestType = request.GetType();
        var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));

        var handler = serviceProvider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No stream handler registered for {requestType.Name}.");

        var stream = (IAsyncEnumerable<TResponse>)handlerType.GetMethod("Handle")!
            .Invoke(handler, [request, cancellationToken])!;

        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    public Task<TResponse> SendCommand<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        => Send(command, cancellationToken);

    public async Task SendCommand(ICommand command, CancellationToken cancellationToken = default)
        => await Send(command, cancellationToken);

    public Task<TResponse> SendQuery<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
        => Send(query, cancellationToken);
}
