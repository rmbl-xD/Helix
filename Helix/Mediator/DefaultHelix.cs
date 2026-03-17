using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Helix;

/// <summary>
/// Default Helix implementation that resolves handlers, behaviors, processors,
/// and exception handlers from DI.
/// When a source-generated <see cref="IHelixDispatchTable"/> is registered (via
/// <c>services.AddHelix(...).UseHelixCodeGen()</c>), the hot path for
/// <see cref="Send{TResponse}"/> and <see cref="CreateStream{TResponse}"/> runs
/// entirely without reflection. The reflection-based path is retained as a fallback
/// for request types not covered by the generated table.
/// </summary>
public sealed class DefaultHelix(IServiceProvider serviceProvider) : IHelix
{
    // Resolved once at construction; null when no generated table/store is registered.
    private readonly IHelixDispatchTable? _dispatchTable    = serviceProvider.GetService<IHelixDispatchTable>();
    private readonly IIdempotencyStore?   _idempotencyStore = serviceProvider.GetService<IIdempotencyStore>();

    // ── Static reflection caches — computed once per unique (requestType, responseType) pair ──

    private static readonly ConcurrentDictionary<(Type, Type), DispatchEntry>  _dispatchCache    = new();
    private static readonly ConcurrentDictionary<(Type, Type), ExceptionEntry> _exceptionCache   = new();
    private static readonly ConcurrentDictionary<(Type, Type), StreamEntry>    _streamCache      = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo?>          _idempotencyCache = new();

    private sealed record DispatchEntry(
        Type        PreProcessorType,      MethodInfo  PreProcessorProcess,
        Type?       NoRespValidatorType,   MethodInfo? NoRespValidatorValidate,
        Type?       ValidatorWithRespType, MethodInfo? ValidatorWithRespValidate,
        Type        HandlerType,           MethodInfo  HandlerHandle,
        Type        BehaviorType,          MethodInfo  BehaviorHandle,
        Type        PostProcessorType,     MethodInfo  PostProcessorProcess);

    private sealed record ExceptionEntry(
        Type ExHandlerType, MethodInfo ExHandlerHandle,
        Type ExActionType,  MethodInfo ExActionExecute);

    private sealed record StreamEntry(
        Type HandlerType, MethodInfo HandlerHandle);

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            // 0. Idempotency check
            Guid? idempotencyKey = null;

            if (_idempotencyStore is not null)
            {
                if (request is IIdempotentCommand idempotentCmd)
                {
                    idempotencyKey = idempotentCmd.IdempotencyKey;
                }
                else
                {
                    var keyProp = _idempotencyCache.GetOrAdd(request.GetType(), static t =>
                        t.GetInterfaces()
                         .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIdempotentCommand<>))
                         ?.GetProperty("IdempotencyKey"));

                    if (keyProp is not null)
                        idempotencyKey = (Guid)keyProp.GetValue(request)!;
                }

                if (idempotencyKey.HasValue && await _idempotencyStore.ExistsAsync(idempotencyKey.Value, cancellationToken))
                    return (await _idempotencyStore.GetResponseAsync<TResponse>(idempotencyKey.Value, cancellationToken))!;
            }

            // Core dispatch: generated (zero reflection) or reflection fallback
            TResponse response;
            if (_dispatchTable is not null && _dispatchTable.TryDispatch(request, cancellationToken, out var generated))
                response = await generated;
            else
                response = await DispatchViaReflection(request, cancellationToken);

            // 4b. Save idempotency
            if (idempotencyKey.HasValue)
                await _idempotencyStore!.SaveAsync(idempotencyKey.Value, response, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            // requestType is only computed on the error path
            var requestType = request.GetType();
            var entry = _exceptionCache.GetOrAdd((requestType, typeof(TResponse)), static key =>
            {
                var (reqType, respType) = key;
                var exHandlerType = typeof(IRequestExceptionHandler<,>).MakeGenericType(reqType, respType);
                var exActionType  = typeof(IRequestExceptionAction<>).MakeGenericType(reqType);
                return new ExceptionEntry(
                    exHandlerType, exHandlerType.GetMethod("Handle")!,
                    exActionType,  exActionType.GetMethod("Execute")!);
            });

            // 5. Exception handlers — can recover by supplying a replacement response
            var state = new RequestExceptionHandlerState<TResponse>();

            foreach (var exHandler in serviceProvider.GetServices(entry.ExHandlerType))
            {
                await (Task)entry.ExHandlerHandle.Invoke(exHandler, [request, ex, state, cancellationToken])!;

                if (state.Handled)
                    break;
            }

            // 6. Exception actions — side effects (logging, metrics) that always run
            foreach (var exAction in serviceProvider.GetServices(entry.ExActionType))
            {
                await (Task)entry.ExActionExecute.Invoke(exAction, [request, ex, cancellationToken])!;
            }

            if (state.Handled)
                return state.Response!;

            throw;
        }
    }

    // Reflection-based fallback — used when no generated dispatch table is present
    // or for request types not covered by the table.
    private async Task<TResponse> DispatchViaReflection<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        var requestType = request.GetType();

        var entry = _dispatchCache.GetOrAdd((requestType, typeof(TResponse)), static key =>
        {
            var (reqType, respType) = key;

            var preProcessorType  = typeof(IRequestPreProcessor<>).MakeGenericType(reqType);
            var handlerType       = typeof(IRequestHandler<,>).MakeGenericType(reqType, respType);
            var behaviorType      = typeof(IPipelineBehavior<,>).MakeGenericType(reqType, respType);
            var postProcessorType = typeof(IRequestPostProcessor<,>).MakeGenericType(reqType, respType);

            // ICommandValidator<TCommand> has constraint: where TCommand : ICommand
            Type? noRespValidatorType = null;
            MethodInfo? noRespValidatorValidate = null;
            if (typeof(ICommand).IsAssignableFrom(reqType))
            {
                noRespValidatorType    = typeof(ICommandValidator<>).MakeGenericType(reqType);
                noRespValidatorValidate = noRespValidatorType.GetMethod("Validate")!;
            }

            // ICommandValidator<TCommand, TResponse> has constraint: where TCommand : ICommand<TResponse>
            Type? validatorWithRespType = null;
            MethodInfo? validatorWithRespValidate = null;
            if (reqType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            {
                validatorWithRespType    = typeof(ICommandValidator<,>).MakeGenericType(reqType, respType);
                validatorWithRespValidate = validatorWithRespType.GetMethod("Validate")!;
            }

            return new DispatchEntry(
                preProcessorType,    preProcessorType.GetMethod("Process")!,
                noRespValidatorType, noRespValidatorValidate,
                validatorWithRespType, validatorWithRespValidate,
                handlerType,         handlerType.GetMethod("Handle")!,
                behaviorType,        behaviorType.GetMethod("Handle")!,
                postProcessorType,   postProcessorType.GetMethod("Process")!);
        });

        // 1. Pre-processors
        foreach (var preProcessor in serviceProvider.GetServices(entry.PreProcessorType))
        {
            await (Task)entry.PreProcessorProcess.Invoke(preProcessor, [request, cancellationToken])!;
        }

        // 1b. Command validators
        var failures = new List<ValidationFailure>();

        if (entry.NoRespValidatorType is not null)
        {
            foreach (var validator in serviceProvider.GetServices(entry.NoRespValidatorType))
            {
                var result = await (Task<ValidationResult>)entry.NoRespValidatorValidate!.Invoke(validator, [request, cancellationToken])!;
                failures.AddRange(result.Errors);
            }
        }

        if (entry.ValidatorWithRespType is not null)
        {
            foreach (var validator in serviceProvider.GetServices(entry.ValidatorWithRespType))
            {
                var result = await (Task<ValidationResult>)entry.ValidatorWithRespValidate!.Invoke(validator, [request, cancellationToken])!;
                failures.AddRange(result.Errors);
            }
        }

        if (failures.Count > 0)
            throw new ValidationException(failures);

        // 2. Resolve handler
        var handler = serviceProvider.GetService(entry.HandlerType)
            ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}.");

        // 3. Build behavior pipeline around the handler
        var behaviors = serviceProvider.GetServices(entry.BehaviorType).ToList();
        behaviors.Reverse();

        RequestHandlerDelegate<TResponse> pipeline = () =>
            (Task<TResponse>)entry.HandlerHandle.Invoke(handler, [request, cancellationToken])!;

        foreach (var behavior in behaviors)
        {
            var current = pipeline;
            pipeline = () => (Task<TResponse>)entry.BehaviorHandle.Invoke(behavior, [request, current, cancellationToken])!;
        }

        var response = await pipeline();

        // 4. Post-processors
        foreach (var postProcessor in serviceProvider.GetServices(entry.PostProcessorType))
        {
            await (Task)entry.PostProcessorProcess.Invoke(postProcessor, [request, response, cancellationToken])!;
        }

        return response;
    }

    // TNotification is the concrete type at the call site — GetServices<> resolves
    // the right handlers directly, eliminating GetType() + MakeGenericType + MethodInfo.Invoke.
    public async Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        foreach (var handler in serviceProvider.GetServices<INotificationHandler<TNotification>>())
            await handler.Handle(notification, cancellationToken);
    }

    public async IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IAsyncEnumerable<TResponse> stream;

        if (_dispatchTable is not null && _dispatchTable.TryDispatchStream(request, cancellationToken, out var generated))
        {
            stream = generated;
        }
        else
        {
            var requestType = request.GetType();
            var entry = _streamCache.GetOrAdd((requestType, typeof(TResponse)), static key =>
            {
                var (reqType, respType) = key;
                var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(reqType, respType);
                return new StreamEntry(handlerType, handlerType.GetMethod("Handle")!);
            });

            var handler = serviceProvider.GetService(entry.HandlerType)
                ?? throw new InvalidOperationException($"No stream handler registered for {requestType.Name}.");
            stream = (IAsyncEnumerable<TResponse>)entry.HandlerHandle.Invoke(handler, [request, cancellationToken])!;
        }

        await foreach (var item in stream.WithCancellation(cancellationToken))
            yield return item;
    }

    public Task<TResponse> SendCommand<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
        => Send(command, cancellationToken);

    public Task SendCommand(ICommand command, CancellationToken ct = default)
        => Send(command, ct);

    public Task<TResponse> SendQuery<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
        => Send(query, cancellationToken);
}
