using System.Runtime.CompilerServices;
using Helix;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHelix(typeof(Program).Assembly);
var provider = services.BuildServiceProvider();

var helix = provider.GetRequiredService<IHelix>();

// Send a command (pre-processor → behavior → handler → post-processor)
await helix.Send(new CreateOrderCommand("ORD-001", 3));

// Send a query
var order = await helix.Send(new GetOrderQuery("ORD-001"));
Console.WriteLine($"Query result: Order {order.Id}, Quantity = {order.Quantity}");

// Publish a notification (fans out to all handlers)
Console.WriteLine();
await helix.Publish(new OrderCreatedEvent("ORD-001"));

// Stream results
Console.WriteLine();
await foreach (var item in helix.CreateStream(new GetAllOrdersStream()))
{
    Console.WriteLine($"[STREAM] Order {item.Id}, Quantity = {item.Quantity}");
}

// Exception handling demo
Console.WriteLine();
var fallback = await helix.Send(new FailingQuery());
Console.WriteLine($"Recovered from exception with: {fallback.Id}");

// =============================================================================
// Commands
// =============================================================================

public record CreateOrderCommand(string OrderId, int Quantity) : ICommand;

public class CreateOrderCommandHandler : CommandHandler<CreateOrderCommand>
{
    protected override Task Handle(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [HANDLER] Order created: {command.OrderId} (qty: {command.Quantity})");
        return Task.CompletedTask;
    }
}

// =============================================================================
// Queries
// =============================================================================

public record OrderDto(string Id, int Quantity);

public record GetOrderQuery(string OrderId) : IQuery<OrderDto>;

public class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OrderDto(request.OrderId, 42));
    }
}

// =============================================================================
// Notifications (1:N fan-out)
// =============================================================================

public record OrderCreatedEvent(string OrderId) : INotification;

public class OrderCreatedEmailHandler : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [EMAIL]   Sending confirmation for {notification.OrderId}");
        return Task.CompletedTask;
    }
}

public class OrderCreatedAnalyticsHandler : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [METRICS] Tracking order {notification.OrderId}");
        return Task.CompletedTask;
    }
}

// =============================================================================
// Streaming
// =============================================================================

public record GetAllOrdersStream() : IStreamRequest<OrderDto>;

public class GetAllOrdersStreamHandler : IStreamRequestHandler<GetAllOrdersStream, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetAllOrdersStream request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OrderDto("ORD-001", 10);
        await Task.Delay(100, cancellationToken);
        yield return new OrderDto("ORD-002", 20);
        await Task.Delay(100, cancellationToken);
        yield return new OrderDto("ORD-003", 30);
    }
}

// =============================================================================
// Pre / Post Processors
// =============================================================================

public class CreateOrderPreProcessor : IRequestPreProcessor<CreateOrderCommand>
{
    public Task Process(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [PRE]     Validating order {request.OrderId}");
        return Task.CompletedTask;
    }
}

public class CreateOrderPostProcessor : IRequestPostProcessor<CreateOrderCommand, Unit>
{
    public Task Process(CreateOrderCommand request, Unit response, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [POST]    Order {request.OrderId} processing complete");
        return Task.CompletedTask;
    }
}

// =============================================================================
// Pipeline Behaviors
// =============================================================================

public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [LOG]     Handling {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"  [LOG]     Handled  {typeof(TRequest).Name}");
        return response;
    }
}

// =============================================================================
// Exception Handling
// =============================================================================

public record FailingQuery() : IQuery<OrderDto>;

public class FailingQueryHandler : IQueryHandler<FailingQuery, OrderDto>
{
    public Task<OrderDto> Handle(FailingQuery request, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Something went wrong!");
    }
}

public class FailingQueryExceptionHandler : IRequestExceptionHandler<FailingQuery, OrderDto>
{
    public Task Handle(FailingQuery request, Exception exception, RequestExceptionHandlerState<OrderDto> state, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [RECOVER] Caught: {exception.Message} — returning fallback");
        state.SetHandled(new OrderDto("FALLBACK", 0));
        return Task.CompletedTask;
    }
}

public class FailingQueryExceptionAction : IRequestExceptionAction<FailingQuery>
{
    public Task Execute(FailingQuery request, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [ACTION]  Logged exception: {exception.Message}");
        return Task.CompletedTask;
    }
}
