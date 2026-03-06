# Helix

A lightweight mediator library implementing the **handler pattern** for **CQRS** (Command Query Responsibility Segregation) with a composable **request/response pipeline**, **notifications**, **streaming**, **pre/post processors**, and **exception handling**.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Setup](#setup)
- [Core Concepts](#core-concepts)
  - [Requests](#requests)
  - [Commands](#commands)
  - [Queries](#queries)
  - [Handlers](#handlers)
  - [Pipeline Behaviors](#pipeline-behaviors)
  - [Notifications](#notifications)
  - [Streaming](#streaming)
  - [Pre-Processors](#pre-processors)
  - [Post-Processors](#post-processors)
  - [Exception Handlers](#exception-handlers)
  - [Exception Actions](#exception-actions)
- [How the Pipeline Works](#how-the-pipeline-works)
- [API Reference](#api-reference)
- [Examples](#examples)
  - [Command with No Return Value](#command-with-no-return-value)
  - [Command with a Return Value](#command-with-a-return-value)
  - [Query](#query)
  - [Open Generic Pipeline Behavior](#open-generic-pipeline-behavior)
  - [Closed Pipeline Behavior for a Specific Request](#closed-pipeline-behavior-for-a-specific-request)
  - [Stacking Multiple Behaviors](#stacking-multiple-behaviors)
  - [Notification (Publish/Subscribe)](#notification-publishsubscribe)
  - [Streaming with IAsyncEnumerable](#streaming-with-iasyncenumerable)
  - [Pre-Processor](#pre-processor)
  - [Post-Processor](#post-processor)
  - [Exception Handler (Recovery)](#exception-handler-recovery)
  - [Exception Action (Side-Effect)](#exception-action-side-effect)
- [File Structure](#file-structure)

---

## Architecture Overview

### Request Pipeline (`Send`)

```
IHelix.Send(request)
    │
    ▼
┌─ Pre-Processors ───────────────────┐
│  IRequestPreProcessor<TRequest>     │  ← Validation, enrichment
└─────────────────────────────────────┘
    │
    ▼
┌─ Pipeline Behaviors ───────────────┐
│  ┌──────────────────────────────┐  │
│  │  Behavior 1 (before)         │  │  ← e.g. LoggingBehavior
│  │  ┌────────────────────────┐  │  │
│  │  │  Behavior 2 (before)   │  │  │  ← e.g. ValidationBehavior
│  │  │  ┌──────────────────┐  │  │  │
│  │  │  │  Handler          │  │  │  │  ← ICommandHandler / IQueryHandler
│  │  │  └──────────────────┘  │  │  │
│  │  │  Behavior 2 (after)    │  │  │
│  │  └────────────────────────┘  │  │
│  │  Behavior 1 (after)          │  │
│  └──────────────────────────────┘  │
└─────────────────────────────────────┘
    │
    ▼
┌─ Post-Processors ──────────────────┐
│  IRequestPostProcessor<TReq, TRes>  │  ← Auditing, cache population
└─────────────────────────────────────┘
    │
    ▼
  TResponse

On exception at any stage:
  → IRequestExceptionHandler  (can recover with a replacement response)
  → IRequestExceptionAction   (side-effects: logging, metrics — always runs)
```

### Notification Pipeline (`Publish`)

```
IHelix.Publish(notification)  →  Handler 1 → Handler 2 → ...  (sequential fan-out)
```

### Stream Pipeline (`CreateStream`)

```
IHelix.CreateStream(request)  →  IStreamRequestHandler  →  IAsyncEnumerable<T>
```

---

## Setup

### 1. Register services

Call `AddHelix()` on your `IServiceCollection`, passing the assemblies that contain your handlers, behaviors, and processors:

```csharp
using Helix;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHelix(typeof(Program).Assembly);
var provider = services.BuildServiceProvider();
```

`AddHelix()` automatically scans the provided assemblies and registers:

| What | Lifetime |
|---|---|
| `IHelix` → `DefaultHelix` | Transient |
| All `IRequestHandler<TRequest, TResponse>` | Transient |
| All `IPipelineBehavior<TRequest, TResponse>` | Transient |
| All `INotificationHandler<TNotification>` | Transient |
| All `IStreamRequestHandler<TRequest, TResponse>` | Transient |
| All `IRequestPreProcessor<TRequest>` | Transient |
| All `IRequestPostProcessor<TRequest, TResponse>` | Transient |
| All `IRequestExceptionHandler<TRequest, TResponse>` | Transient |
| All `IRequestExceptionAction<TRequest>` | Transient |

If no assemblies are passed, it defaults to the **calling assembly**.

### 2. Resolve and use Helix

```csharp
var helix = provider.GetRequiredService<IHelix>();

await helix.Send(new MyCommand("data"));
var result = await helix.Send(new MyQuery(42));
await helix.Publish(new OrderCreatedEvent("ORD-001"));

await foreach (var item in helix.CreateStream(new GetAllItemsStream()))
{
    Console.WriteLine(item);
}
```

---

## Core Concepts

### Requests

`IRequest<TResponse>` is the base marker interface for anything that can be sent through Helix. All commands and queries derive from it.

```csharp
public interface IRequest<out TResponse>;   // Request with a response
public interface IRequest : IRequest<Unit>; // Request with no response (returns Unit)
```

`Unit` is a value type representing "void" — it allows the pipeline to remain generic over `TResponse` even when there is nothing meaningful to return.

### Commands

Commands represent **write operations** (side effects). Use them when the intent is to change state.

```csharp
public interface ICommand : IRequest<Unit>;                     // No return value
public interface ICommand<out TResponse> : IRequest<TResponse>; // With return value
```

### Queries

Queries represent **read operations**. They always return a value and should not produce side effects.

```csharp
public interface IQuery<out TResponse> : IRequest<TResponse>;
```

### Handlers

Every request type has exactly **one** handler. The handler contains the business logic for that request.

| Interface | Use Case |
|---|---|
| `IRequestHandler<TRequest, TResponse>` | General-purpose handler |
| `ICommandHandler<TCommand>` | Command with no return value |
| `ICommandHandler<TCommand, TResponse>` | Command with a return value |
| `IQueryHandler<TQuery, TResponse>` | Query handler |

For commands with no return value, inherit from the `CommandHandler<TCommand>` (or `RequestHandler<TRequest>`) **base class** to avoid manually returning `Unit`:

```csharp
public class MyHandler : CommandHandler<MyCommand>
{
    protected override Task Handle(MyCommand command, CancellationToken cancellationToken)
    {
        // Your logic here — no need to return anything
        return Task.CompletedTask;
    }
}
```

For handlers that return a value, implement the interface directly:

```csharp
public class MyHandler : IQueryHandler<MyQuery, MyDto>
{
    public Task<MyDto> Handle(MyQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new MyDto(...));
    }
}
```

### Pipeline Behaviors

Pipeline behaviors are **middleware** that wrap the handler. They execute in registration order and can run logic before and/or after the inner handler.

```csharp
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default);
}
```

- `request` — the incoming request object.
- `next` — a delegate that invokes the next behavior in the chain (or the handler itself if this is the innermost behavior).
- Call `await next()` to continue the pipeline. Skip calling it to short-circuit.

There are two kinds of behaviors:

| Kind | Description | Example |
|---|---|---|
| **Open generic** | Applies to **all** requests. Defined with open type parameters `<TRequest, TResponse>`. | `LoggingBehavior<TRequest, TResponse>` |
| **Closed** | Applies to a **specific** request type. | `ValidateCreateOrderBehavior : IPipelineBehavior<CreateOrderCommand, Unit>` |

### Notifications

Notifications implement a **publish/subscribe** (1:N) pattern. Unlike requests, a single notification fans out to **all** registered handlers sequentially.

```csharp
public interface INotification;

public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
```

Use notifications for domain events, cross-cutting side-effects, or any case where multiple subsystems need to react to the same event.

### Streaming

Streaming requests return an `IAsyncEnumerable<TResponse>`, yielding results one at a time. Useful for large data sets, real-time feeds, or progressive loading.

```csharp
public interface IStreamRequest<out TResponse>;

public interface IStreamRequestHandler<in TRequest, out TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}
```

Consume streams with `await foreach`:

```csharp
await foreach (var item in helix.CreateStream(new MyStreamRequest()))
{
    // Process each item as it arrives
}
```

### Pre-Processors

Pre-processors run **before** the pipeline behaviors and handler. Use them for validation, enrichment, or authorization that should always happen first.

```csharp
public interface IRequestPreProcessor<in TRequest>
{
    Task Process(TRequest request, CancellationToken cancellationToken = default);
}
```

Multiple pre-processors for the same request type execute in registration order. Throw an exception to short-circuit before the handler is reached.

### Post-Processors

Post-processors run **after** the handler and pipeline behaviors complete successfully. Use them for auditing, response enrichment, or cache population.

```csharp
public interface IRequestPostProcessor<in TRequest, in TResponse>
{
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken = default);
}
```

Post-processors receive both the original request and the handler's response.

### Exception Handlers

Exception handlers can **recover** from exceptions by providing a replacement response. Multiple handlers are invoked in order; processing stops at the first one that calls `state.SetHandled()`.

```csharp
public interface IRequestExceptionHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task Handle(
        TRequest request,
        Exception exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken = default);
}
```

`RequestExceptionHandlerState<TResponse>` exposes:
- `SetHandled(TResponse response)` — marks the exception as recovered and provides a fallback response.
- `Handled` — whether recovery has occurred.
- `Response` — the replacement response (if handled).

### Exception Actions

Exception actions perform **side-effects** (logging, metrics, alerting) when an exception occurs. They always run — regardless of whether an exception handler recovered.

```csharp
public interface IRequestExceptionAction<in TRequest>
{
    Task Execute(TRequest request, Exception exception, CancellationToken cancellationToken = default);
}
```

Unlike exception handlers, actions cannot recover from exceptions.

---

## How the Pipeline Works

When you call `helix.Send(request)`:

1. All **`IRequestPreProcessor<TRequest>`** instances run in order.
2. **`DefaultHelix`** resolves the single `IRequestHandler<TRequest, TResponse>` from DI. If none is found, an `InvalidOperationException` is thrown.
3. All **`IPipelineBehavior<TRequest, TResponse>`** instances are composed into a Russian-doll chain around the handler.
4. The outermost behavior delegate is invoked, flowing inward to the handler.
5. All **`IRequestPostProcessor<TRequest, TResponse>`** instances run in order with the request and response.
6. The response is returned.

If an exception occurs at **any** stage:

7. All **`IRequestExceptionHandler<TRequest, TResponse>`** instances run in order. The first to call `state.SetHandled(response)` provides a recovery response and stops further handlers.
8. All **`IRequestExceptionAction<TRequest>`** instances run (always, even if recovered).
9. If recovered, the replacement response is returned. Otherwise, the exception propagates.

```
Send(request)
  → PreProcessor1.Process(request)
  → PreProcessor2.Process(request)
  → Behavior1.Handle(request, next: →
      Behavior2.Handle(request, next: →
        Handler.Handle(request)))
  → PostProcessor1.Process(request, response)
  → PostProcessor2.Process(request, response)
  → return response
```

When you call `helix.Publish(notification)`:

- All **`INotificationHandler<TNotification>`** instances execute **sequentially** in registration order.

When you call `helix.CreateStream(request)`:

- The single **`IStreamRequestHandler<TRequest, TResponse>`** is resolved and its `Handle` method returns an `IAsyncEnumerable<TResponse>` that is forwarded to the caller.

---

## API Reference

### `IHelix`

```csharp
public interface IHelix
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification;
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|---|---|
| `Send` | Sends a request through the full pipeline (pre → behaviors → handler → post) to a single handler. |
| `Publish` | Publishes a notification to all registered handlers (1:N fan-out). |
| `CreateStream` | Creates an async stream from a streaming request handler. |

### `ServiceCollectionExtensions.AddHelix()`

```csharp
public static IServiceCollection AddHelix(this IServiceCollection services, params Assembly[] assemblies);
```

Scans the given assemblies (or the calling assembly if none are provided) and registers all Helix types. Open and closed generic implementations are both supported. All registrations use the **transient** lifetime.

### `Unit`

```csharp
public readonly struct Unit
```

A singleton-like value type representing "no value". Use `Unit.Value` when you need an instance. Commands and requests with no meaningful return type resolve to `Unit`.

### `RequestExceptionHandlerState<TResponse>`

```csharp
public class RequestExceptionHandlerState<TResponse>
{
    public TResponse? Response { get; }
    public bool Handled { get; }
    public void SetHandled(TResponse response);
}
```

Passed to exception handlers to allow recovery. Call `SetHandled` with a replacement response to prevent the exception from propagating.

---

## Examples

### Command with No Return Value

```csharp
public record DeleteOrderCommand(string OrderId) : ICommand;

public class DeleteOrderCommandHandler : CommandHandler<DeleteOrderCommand>
{
    protected override Task Handle(DeleteOrderCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Deleted order {command.OrderId}");
        return Task.CompletedTask;
    }
}

await helix.Send(new DeleteOrderCommand("ORD-099"));
```

### Command with a Return Value

```csharp
public record CreateOrderCommand(string ProductId, int Quantity) : ICommand<Guid>;

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, Guid>
{
    public Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        return Task.FromResult(orderId);
    }
}

Guid newOrderId = await helix.Send(new CreateOrderCommand("PROD-1", 5));
```

### Query

```csharp
public record OrderDto(string Id, string ProductId, int Quantity);
public record GetOrderByIdQuery(string OrderId) : IQuery<OrderDto>;

public class GetOrderByIdQueryHandler : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OrderDto(request.OrderId, "PROD-1", 10));
    }
}

var order = await helix.Send(new GetOrderByIdQuery("ORD-001"));
```

### Open Generic Pipeline Behavior

An open generic behavior runs for **every** request that flows through Helix. Auto-discovered by `AddHelix()`.

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[LOG] Handling {typeof(TRequest).Name}");
        var response = await next();
        Console.WriteLine($"[LOG] Handled  {typeof(TRequest).Name}");
        return response;
    }
}
```

### Closed Pipeline Behavior for a Specific Request

A closed behavior targets a **single** request type. Useful for validation, authorization, etc.

```csharp
public class ValidateCreateOrderBehavior : IPipelineBehavior<CreateOrderCommand, Unit>
{
    public async Task<Unit> Handle(
        CreateOrderCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
            throw new ArgumentException("Quantity must be positive.");

        return await next();
    }
}
```

### Stacking Multiple Behaviors

Behaviors execute in **registration order**. You can short-circuit the pipeline by **not** calling `next()`:

```csharp
public class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var cached = TryGetFromCache<TResponse>(request);
        if (cached is not null)
            return cached; // Short-circuit — handler is never called

        var response = await next();
        AddToCache(request, response);
        return response;
    }
}
```

### Notification (Publish/Subscribe)

One notification, multiple handlers. All handlers execute sequentially.

```csharp
public record OrderCreatedEvent(string OrderId) : INotification;

public class SendConfirmationEmail : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Sending email for {notification.OrderId}");
        return Task.CompletedTask;
    }
}

public class TrackAnalytics : INotificationHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Tracking order {notification.OrderId}");
        return Task.CompletedTask;
    }
}

// Both handlers execute
await helix.Publish(new OrderCreatedEvent("ORD-001"));
```

### Streaming with IAsyncEnumerable

Stream multiple results from a single request using `CreateStream` and `await foreach`:

```csharp
public record GetAllOrdersStream() : IStreamRequest<OrderDto>;

public class GetAllOrdersStreamHandler : IStreamRequestHandler<GetAllOrdersStream, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetAllOrdersStream request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OrderDto("ORD-001", "PROD-1", 10);
        await Task.Delay(100, cancellationToken);
        yield return new OrderDto("ORD-002", "PROD-2", 20);
    }
}

await foreach (var order in helix.CreateStream(new GetAllOrdersStream()))
{
    Console.WriteLine($"Streamed: {order.Id}");
}
```

### Pre-Processor

Runs before the pipeline behaviors and handler. Useful for validation or request enrichment.

```csharp
public class ValidateOrder : IRequestPreProcessor<CreateOrderCommand>
{
    public Task Process(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId))
            throw new ArgumentException("OrderId is required.");

        Console.WriteLine($"[PRE] Validated order {request.OrderId}");
        return Task.CompletedTask;
    }
}
```

### Post-Processor

Runs after the handler completes. Receives both the request and the response.

```csharp
public class AuditOrder : IRequestPostProcessor<CreateOrderCommand, Unit>
{
    public Task Process(CreateOrderCommand request, Unit response, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[POST] Audit: order {request.OrderId} created");
        return Task.CompletedTask;
    }
}
```

### Exception Handler (Recovery)

Recover from exceptions by providing a fallback response. Processing stops at the first handler that calls `SetHandled`.

```csharp
public record FailingQuery() : IQuery<OrderDto>;

public class FailingQueryHandler : IQueryHandler<FailingQuery, OrderDto>
{
    public Task<OrderDto> Handle(FailingQuery request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("Database unavailable");
}

public class FailingQueryRecovery : IRequestExceptionHandler<FailingQuery, OrderDto>
{
    public Task Handle(
        FailingQuery request, Exception exception,
        RequestExceptionHandlerState<OrderDto> state, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[RECOVER] {exception.Message} — returning cached data");
        state.SetHandled(new OrderDto("CACHED", "N/A", 0));
        return Task.CompletedTask;
    }
}

// Returns the fallback OrderDto instead of throwing
var result = await helix.Send(new FailingQuery());
```

### Exception Action (Side-Effect)

Performs logging or metrics on exception. Always runs, even if an exception handler recovered.

```csharp
public class LogFailingQueryException : IRequestExceptionAction<FailingQuery>
{
    public Task Execute(FailingQuery request, Exception exception, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[ACTION] Logged exception: {exception.Message}");
        return Task.CompletedTask;
    }
}
```

---

## File Structure

```
Helix/
└── Mediator/
    ├── Unit.cs                        # Void return type
    ├── IRequest.cs                    # Base request marker interfaces
    ├── ICommand.cs                    # Command marker interfaces (CQRS write)
    ├── IQuery.cs                      # Query marker interface (CQRS read)
    ├── IRequestHandler.cs             # Handler interfaces + RequestHandler base class
    ├── ICommandHandler.cs             # Command handler interfaces + CommandHandler base class
    ├── IQueryHandler.cs               # Query handler interface
    ├── IPipelineBehavior.cs           # Pipeline behavior interface + delegate
    ├── INotification.cs               # Notification + notification handler (pub/sub)
    ├── IStreamRequest.cs              # Stream request + stream handler (IAsyncEnumerable)
    ├── IRequestPreProcessor.cs        # Pre-processor interface
    ├── IRequestPostProcessor.cs       # Post-processor interface
    ├── IRequestExceptionHandler.cs    # Exception handler + state (recovery)
    ├── IRequestExceptionAction.cs     # Exception action (side-effects)
    ├── IHelix.cs                      # Helix contract (Send, Publish, CreateStream)
    ├── DefaultHelix.cs                # Helix implementation (full pipeline)
    └── ServiceCollectionExtensions.cs # AddHelix() registration + assembly scanning
```
