# Helix

> **Helix** combines the best ideas from [MediatR](https://github.com/jbogard/MediatR) and the mediator parts of [Wolverine](https://github.com/JasperFx/wolverine) into a single, lightweight library with first-class CQRS support.
>
> From **MediatR** it takes the clean request/response pipeline, pipeline behaviors (Russian-doll middleware), notifications (publish/subscribe), and pre/post processors.
> From **Wolverine** it draws inspiration for explicit command/query separation with dedicated handler types, command-specific validation, and typed dispatch methods that make the intent of every call — read or write — unmistakable at the call site.
>
> The result is a **composable mediator** that gives you a rich, opinionated CQRS pipeline out of the box — including command/query-specific behaviors, built-in validation, domain events, streaming queries, and idempotency — while staying lightweight and easy to extend.

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
  - [Command & Query Behaviors](#command--query-behaviors)
  - [Command Validation](#command-validation)
  - [Notifications](#notifications)
  - [Domain Events](#domain-events)
  - [Streaming](#streaming)
  - [Stream Queries](#stream-queries)
  - [Pre-Processors](#pre-processors)
  - [Post-Processors](#post-processors)
  - [Exception Handlers](#exception-handlers)
  - [Exception Actions](#exception-actions)
  - [Idempotency](#idempotency)
- [How the Pipeline Works](#how-the-pipeline-works)
- [API Reference](#api-reference)
- [Examples](#examples)
  - [Command with No Return Value](#command-with-no-return-value)
  - [Command with a Return Value](#command-with-a-return-value)
  - [Query](#query)
  - [Typed Dispatch (SendCommand / SendQuery)](#typed-dispatch-sendcommand--sendquery)
  - [Open Generic Pipeline Behavior](#open-generic-pipeline-behavior)
  - [Command-Specific Behavior](#command-specific-behavior)
  - [Query-Specific Behavior](#query-specific-behavior)
  - [Closed Pipeline Behavior for a Specific Request](#closed-pipeline-behavior-for-a-specific-request)
  - [Stacking Multiple Behaviors](#stacking-multiple-behaviors)
  - [Command Validation](#command-validation-1)
  - [Notification (Publish/Subscribe)](#notification-publishsubscribe)
  - [Domain Event](#domain-event)
  - [Streaming with IAsyncEnumerable](#streaming-with-iasyncenumerable)
  - [Stream Query](#stream-query)
  - [Pre-Processor](#pre-processor)
  - [Post-Processor](#post-processor)
  - [Exception Handler (Recovery)](#exception-handler-recovery)
  - [Exception Action (Side-Effect)](#exception-action-side-effect)
  - [Idempotent Command](#idempotent-command)
- [File Structure](#file-structure)

---

## Architecture Overview

### Request Pipeline (`Send` / `SendCommand` / `SendQuery`)

```
IHelix.Send(request)  /  SendCommand(command)  /  SendQuery(query)
    │
    ▼
┌─ Idempotency Check ────────────────┐
│  IIdempotencyStore (if registered)  │  ← Return cached response for duplicate commands
└─────────────────────────────────────┘
    │
    ▼
┌─ Pre-Processors ───────────────────┐
│  IRequestPreProcessor<TRequest>     │  ← Enrichment, normalization
└─────────────────────────────────────┘
    │
    ▼
┌─ Command Validators ───────────────┐
│  ICommandValidator<TCommand>        │  ← Aggregated validation (commands only)
│  ICommandValidator<TCmd, TResponse> │  ← Throws ValidationException on failure
└─────────────────────────────────────┘
    │
    ▼
┌─ Pipeline Behaviors ───────────────┐
│  ┌──────────────────────────────┐  │
│  │  Behavior 1 (before)         │  │  ← IPipelineBehavior / ICommandBehavior / IQueryBehavior
│  │  ┌────────────────────────┐  │  │
│  │  │  Behavior 2 (before)   │  │  │
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
┌─ Idempotency Save ─────────────────┐
│  IIdempotencyStore (if applicable)  │  ← Persist response for future deduplication
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

Works for both `INotification` and `IDomainEvent` types.

### Stream Pipeline (`CreateStream`)

```
IHelix.CreateStream(request)  →  IStreamRequestHandler  →  IAsyncEnumerable<T>
```

Works for both `IStreamRequest<T>` and `IStreamQuery<T>` types.

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
| All `IPipelineBehavior<TRequest, TResponse>` (incl. `ICommandBehavior`, `IQueryBehavior`) | Transient |
| All `INotificationHandler<TNotification>` (incl. `IDomainEventHandler`) | Transient |
| All `IStreamRequestHandler<TRequest, TResponse>` (incl. `IStreamQueryHandler`) | Transient |
| All `IRequestPreProcessor<TRequest>` | Transient |
| All `IRequestPostProcessor<TRequest, TResponse>` | Transient |
| All `IRequestExceptionHandler<TRequest, TResponse>` | Transient |
| All `IRequestExceptionAction<TRequest>` | Transient |
| All `ICommandValidator<TCommand>` / `ICommandValidator<TCommand, TResponse>` | Transient |

If no assemblies are passed, it defaults to the **calling assembly**.

### 2. Resolve and use Helix

```csharp
var helix = provider.GetRequiredService<IHelix>();

// Generic dispatch
await helix.Send(new MyCommand("data"));
var result = await helix.Send(new MyQuery(42));

// Typed CQRS dispatch (compile-time intent safety)
await helix.SendCommand(new MyCommand("data"));
var order = await helix.SendQuery(new GetOrderQuery("ORD-001"));

// Notifications & domain events
await helix.Publish(new OrderCreatedEvent("ORD-001"));

// Streaming
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

For commands with no return value, inherit from the `CommandHandler<TCommand>` base class to avoid manually returning `Unit`:

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

For queries, you can use the `QueryHandler<TQuery, TResponse>` base class or implement `IQueryHandler<TQuery, TResponse>` directly:

```csharp
public class MyHandler : QueryHandler<MyQuery, MyDto>
{
    public override Task<MyDto> Handle(MyQuery query, CancellationToken cancellationToken)
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

### Command & Query Behaviors

For CQRS architectures you often want behaviors that apply **only** to commands or **only** to queries. Helix provides constrained behavior interfaces that are automatically discovered alongside regular `IPipelineBehavior` implementations:

```csharp
// Applies only to commands — e.g. transaction wrapping, audit logging
public interface ICommandBehavior<in TCommand, TResponse> : IPipelineBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

// Applies only to queries — e.g. caching, read-replica routing
public interface IQueryBehavior<in TQuery, TResponse> : IPipelineBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
```

Because they extend `IPipelineBehavior`, they participate in the standard behavior pipeline without any extra configuration.

### Command Validation

Helix has built-in support for command validation. Register one or more `ICommandValidator` implementations per command type — they run automatically after pre-processors and **before** the handler. If any validator reports errors, a `ValidationException` is thrown and the handler is never invoked.

```csharp
// For commands with no return value
public interface ICommandValidator<in TCommand>
    where TCommand : ICommand
{
    Task<ValidationResult> Validate(TCommand command, CancellationToken cancellationToken = default);
}

// For commands with a return value
public interface ICommandValidator<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<ValidationResult> Validate(TCommand command, CancellationToken cancellationToken = default);
}
```

Supporting types:

| Type | Purpose |
|---|---|
| `ValidationResult` | Holds a list of `Errors`. Check `IsValid` to see if validation passed. |
| `ValidationFailure` | A `record(string PropertyName, string ErrorMessage)` describing a single failure. |
| `ValidationException` | Thrown when `Errors` is non-empty. Exposes an `IReadOnlyList<ValidationFailure> Errors` property. |

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

Use notifications for cross-cutting side-effects or any case where multiple subsystems need to react to the same event.

### Domain Events

Domain events are a specialization of notifications that represent **something that already happened** in the domain. They carry a timestamp and are dispatched through the standard notification pipeline.

```csharp
public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}

public interface IDomainEventHandler<in TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent;
```

Because `IDomainEvent` extends `INotification`, domain event handlers are discovered and invoked by `Publish` without any additional wiring.

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

### Stream Queries

Stream queries combine streaming with read-side CQRS semantics. Use them for paginated cursors, real-time feeds, or any read-side operation that yields multiple results over time.

```csharp
public interface IStreamQuery<out TResponse> : IStreamRequest<TResponse>;

public interface IStreamQueryHandler<in TQuery, out TResponse> : IStreamRequestHandler<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>;
```

Because they extend `IStreamRequest`, stream queries work with the existing `CreateStream` method.

### Pre-Processors

Pre-processors run **before** the validators, pipeline behaviors, and handler. Use them for enrichment, normalization, or authorization that should always happen first.

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

### Idempotency

Helix supports automatic command deduplication for distributed systems. Mark a command with `IIdempotentCommand` (or `IIdempotentCommand<TResponse>`) and register an `IIdempotencyStore` — the pipeline checks for duplicates before execution and caches results after.

```csharp
// For commands with no return value
public interface IIdempotentCommand : ICommand
{
    Guid IdempotencyKey { get; }
}

// For commands with a return value
public interface IIdempotentCommand<out TResponse> : ICommand<TResponse>
{
    Guid IdempotencyKey { get; }
}
```

The `IIdempotencyStore` is a user-provided storage abstraction:

```csharp
public interface IIdempotencyStore
{
    Task<bool> ExistsAsync(Guid idempotencyKey, CancellationToken cancellationToken = default);
    Task<TResponse?> GetResponseAsync<TResponse>(Guid idempotencyKey, CancellationToken cancellationToken = default);
    Task SaveAsync<TResponse>(Guid idempotencyKey, TResponse response, CancellationToken cancellationToken = default);
}
```

When no `IIdempotencyStore` is registered, idempotency is a no-op.

---

## How the Pipeline Works

When you call `helix.Send(request)` (or `SendCommand` / `SendQuery`):

1. **Idempotency check** — If the request is an `IIdempotentCommand` and an `IIdempotencyStore` is registered, the store is queried. If a cached response exists, it is returned immediately.
2. All **`IRequestPreProcessor<TRequest>`** instances run in order.
3. All **`ICommandValidator<TCommand>`** instances run (commands only). Failures are aggregated; if any exist, a `ValidationException` is thrown.
4. **`DefaultHelix`** resolves the single `IRequestHandler<TRequest, TResponse>` from DI. If none is found, an `InvalidOperationException` is thrown.
5. All **`IPipelineBehavior<TRequest, TResponse>`** instances (including `ICommandBehavior` and `IQueryBehavior`) are composed into a Russian-doll chain around the handler.
6. The outermost behavior delegate is invoked, flowing inward to the handler.
7. All **`IRequestPostProcessor<TRequest, TResponse>`** instances run in order with the request and response.
8. **Idempotency save** — If the request was idempotent, the response is stored for future deduplication.
9. The response is returned.

If an exception occurs at **any** stage:

10. All **`IRequestExceptionHandler<TRequest, TResponse>`** instances run in order. The first to call `state.SetHandled(response)` provides a recovery response and stops further handlers.
11. All **`IRequestExceptionAction<TRequest>`** instances run (always, even if recovered).
12. If recovered, the replacement response is returned. Otherwise, the exception propagates.

```
Send(request)
  → Idempotency check (return cached if duplicate)
  → PreProcessor1.Process(request)
  → PreProcessor2.Process(request)
  → Validator1.Validate(command) → Validator2.Validate(command) → throw if errors
  → Behavior1.Handle(request, next: →
      Behavior2.Handle(request, next: →
        Handler.Handle(request)))
  → PostProcessor1.Process(request, response)
  → PostProcessor2.Process(request, response)
  → Idempotency save
  → return response
```

When you call `helix.Publish(notification)`:

- All **`INotificationHandler<TNotification>`** instances (including `IDomainEventHandler` implementations) execute **sequentially** in registration order.

When you call `helix.CreateStream(request)`:

- The single **`IStreamRequestHandler<TRequest, TResponse>`** (or `IStreamQueryHandler`) is resolved and its `Handle` method returns an `IAsyncEnumerable<TResponse>` that is forwarded to the caller.

---

## API Reference

### `IHelix`

```csharp
public interface IHelix
{
    // Generic dispatch
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    // Typed CQRS dispatch
    Task<TResponse> SendCommand<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
    Task SendCommand(ICommand command, CancellationToken cancellationToken = default);
    Task<TResponse> SendQuery<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);

    // Notifications
    Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    // Streaming
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|---|---|
| `Send` | Sends a request through the full pipeline (idempotency → pre → validation → behaviors → handler → post → idempotency save) to a single handler. |
| `SendCommand` | Type-safe dispatch for commands. Delegates to `Send`. |
| `SendQuery` | Type-safe dispatch for queries. Delegates to `Send`. |
| `Publish` | Publishes a notification or domain event to all registered handlers (1:N fan-out). |
| `CreateStream` | Creates an async stream from a streaming request or stream query handler. |

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

public class GetOrderByIdQueryHandler : QueryHandler<GetOrderByIdQuery, OrderDto>
{
    public override Task<OrderDto> Handle(GetOrderByIdQuery query, CancellationToken cancellationToken)
    {
        return Task.FromResult(new OrderDto(query.OrderId, "PROD-1", 10));
    }
}

var order = await helix.Send(new GetOrderByIdQuery("ORD-001"));
```

### Typed Dispatch (SendCommand / SendQuery)

Use the typed dispatch methods for compile-time safety that separates reads from writes:

```csharp
// Write path — only accepts ICommand / ICommand<TResponse>
await helix.SendCommand(new DeleteOrderCommand("ORD-099"));
Guid id = await helix.SendCommand(new CreateOrderCommand("PROD-1", 5));

// Read path — only accepts IQuery<TResponse>
var order = await helix.SendQuery(new GetOrderByIdQuery("ORD-001"));
```

Both delegate to `Send` internally, so the full pipeline (validation, behaviors, processors) applies.

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

### Command-Specific Behavior

Runs only for commands. Use for transaction wrapping, audit logging, or any write-side concern.

```csharp
public class TransactionBehavior<TCommand, TResponse> : ICommandBehavior<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<TResponse> Handle(
        TCommand request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        Console.WriteLine("[TX] Begin");
        var response = await next();
        Console.WriteLine("[TX] Commit");
        return response;
    }
}
```

### Query-Specific Behavior

Runs only for queries. Use for caching, read-replica routing, or any read-side concern.

```csharp
public class CachingBehavior<TQuery, TResponse> : IQueryBehavior<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    public async Task<TResponse> Handle(
        TQuery request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        // Check cache, return if found, otherwise call next() and cache the result
        return await next();
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

### Command Validation

Register validators per command type. All validators run and failures are aggregated into a single `ValidationException`:

```csharp
public record PlaceOrderCommand(string OrderId, int Quantity) : ICommand;

public class PlaceOrderValidator : ICommandValidator<PlaceOrderCommand>
{
    public Task<ValidationResult> Validate(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(command.OrderId))
            result.Errors.Add(new ValidationFailure("OrderId", "OrderId is required."));

        if (command.Quantity <= 0)
            result.Errors.Add(new ValidationFailure("Quantity", "Quantity must be positive."));

        return Task.FromResult(result);
    }
}

// If validation fails, a ValidationException is thrown with all errors:
try
{
    await helix.SendCommand(new PlaceOrderCommand("", -1));
}
catch (ValidationException ex)
{
    foreach (var error in ex.Errors)
        Console.WriteLine($"{error.PropertyName}: {error.ErrorMessage}");
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

### Domain Event

Domain events carry a timestamp and express that something meaningful happened in the domain:

```csharp
public record OrderShippedEvent(string OrderId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}

public class NotifyCustomerOfShipment : IDomainEventHandler<OrderShippedEvent>
{
    public Task Handle(OrderShippedEvent notification, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Order {notification.OrderId} shipped at {notification.OccurredOn}");
        return Task.CompletedTask;
    }
}

await helix.Publish(new OrderShippedEvent("ORD-001"));
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

### Stream Query

A stream query combines streaming with read-side CQRS semantics:

```csharp
public record GetRecentOrdersQuery() : IStreamQuery<OrderDto>;

public class GetRecentOrdersQueryHandler : IStreamQueryHandler<GetRecentOrdersQuery, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetRecentOrdersQuery request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new OrderDto("ORD-001", "PROD-1", 10);
        await Task.Delay(100, cancellationToken);
        yield return new OrderDto("ORD-002", "PROD-2", 20);
    }
}

await foreach (var order in helix.CreateStream(new GetRecentOrdersQuery()))
{
    Console.WriteLine($"Recent: {order.Id}");
}
```

### Pre-Processor

Runs before the validators, pipeline behaviors, and handler. Useful for request normalization or enrichment.

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

### Idempotent Command

Prevent duplicate command execution in distributed systems:

```csharp
public record ChargePaymentCommand(Guid IdempotencyKey, string OrderId, decimal Amount) : IIdempotentCommand;

public class ChargePaymentHandler : CommandHandler<ChargePaymentCommand>
{
    protected override Task Handle(ChargePaymentCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Charging {command.Amount:C} for order {command.OrderId}");
        return Task.CompletedTask;
    }
}

// Register your IIdempotencyStore implementation (in-memory, Redis, database, etc.)
services.AddSingleton<IIdempotencyStore, MyIdempotencyStore>();

// First call executes the handler and caches the result
var key = Guid.NewGuid();
await helix.SendCommand(new ChargePaymentCommand(key, "ORD-001", 99.99m));

// Second call with the same key returns the cached result — handler is NOT invoked again
await helix.SendCommand(new ChargePaymentCommand(key, "ORD-001", 99.99m));
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
    ├── IQueryHandler.cs               # Query handler interface + QueryHandler base class
    ├── IPipelineBehavior.cs           # Pipeline behavior interface + delegate
    ├── ICommandBehavior.cs            # Command-specific pipeline behavior
    ├── IQueryBehavior.cs              # Query-specific pipeline behavior
    ├── ICommandValidator.cs           # Validation: ICommandValidator, ValidationResult, ValidationException
    ├── INotification.cs               # Notification + notification handler (pub/sub)
    ├── IDomainEvent.cs                # Domain event + domain event handler
    ├── IStreamRequest.cs              # Stream request + stream handler (IAsyncEnumerable)
    ├── IStreamQuery.cs                # Stream query + stream query handler (CQRS read streaming)
    ├── IIdempotentCommand.cs          # Idempotent command markers + IIdempotencyStore
    ├── IRequestPreProcessor.cs        # Pre-processor interface
    ├── IRequestPostProcessor.cs       # Post-processor interface
    ├── IRequestExceptionHandler.cs    # Exception handler + state (recovery)
    ├── IRequestExceptionAction.cs     # Exception action (side-effects)
    ├── IHelix.cs                      # Helix contract (Send, SendCommand, SendQuery, Publish, CreateStream)
    ├── DefaultHelix.cs                # Helix implementation (full pipeline)
    └── ServiceCollectionExtensions.cs # AddHelix() registration + assembly scanning
```
