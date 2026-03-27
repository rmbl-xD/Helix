# Helix.CQRS

A lightweight CQRS/Mediator library for .NET with a reflection-free hot path, built-in validation, idempotency, and streaming support.

## Installation

```bash
dotnet add package Helix.CQRS
```

## Setup

Register Helix in your DI container. It auto-discovers all handlers, behaviors, and processors in the given assemblies.

```csharp
builder.Services.AddHelix(typeof(Program).Assembly);
```

## Core Concepts

### Requests

```csharp
public record GetUserQuery(int Id) : IQuery<UserDto>;

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery query, CancellationToken ct)
        => Task.FromResult(new UserDto(query.Id, "Alice"));
}
```

### Commands

```csharp
public record CreateUserCommand(string Name) : ICommand;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand>
{
    public Task Handle(CreateUserCommand command, CancellationToken ct)
    {
        // write logic
        return Task.CompletedTask;
    }
}
```

### Dispatching

```csharp
public class MyService(IHelix helix)
{
    public Task<UserDto> GetUser(int id, CancellationToken ct)
        => helix.SendQuery(new GetUserQuery(id), ct);

    public Task CreateUser(string name, CancellationToken ct)
        => helix.SendCommand(new CreateUserCommand(name), ct);
}
```

### Notifications

```csharp
public record UserCreated(int Id) : INotification;

public class SendWelcomeEmail : INotificationHandler<UserCreated>
{
    public Task Handle(UserCreated notification, CancellationToken ct) { ... }
}

// Publish
await helix.Publish(new UserCreated(42), ct);
```

### Streaming

```csharp
public record LiveFeedRequest : IStreamRequest<FeedItem>;

public class LiveFeedHandler : IStreamRequestHandler<LiveFeedRequest, FeedItem>
{
    public async IAsyncEnumerable<FeedItem> Handle(LiveFeedRequest request,
        [EnumeratorCancellation] CancellationToken ct) { ... }
}

await foreach (var item in helix.CreateStream(new LiveFeedRequest(), ct)) { ... }
```

## Pipeline Behaviors

Wrap any request in a Russian-doll middleware pipeline.

```csharp
public class LoggingBehavior<TRequest, TResponse>(ILogger logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request,
        RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}
```

## Validation

```csharp
public class CreateUserValidator : ICommandValidator<CreateUserCommand>
{
    public Task<ValidationResult> Validate(CreateUserCommand command, CancellationToken ct)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(command.Name))
            result.Errors.Add(new ValidationFailure(nameof(command.Name), "Name is required."));
        return Task.FromResult(result);
    }
}
```

## Idempotent Commands

Prevent duplicate execution by implementing `IIdempotentCommand`. Requires an `IIdempotencyStore` registered in DI.

```csharp
public record PlaceOrderCommand(Guid IdempotencyKey, int ProductId) : IIdempotentCommand;
```

## Pre / Post Processors & Exception Handlers

| Interface | Purpose |
|---|---|
| `IRequestPreProcessor<TRequest>` | Runs before the handler |
| `IRequestPostProcessor<TRequest, TResponse>` | Runs after the handler |
| `IRequestExceptionHandler<TRequest, TResponse>` | Handles and recovers from exceptions |
| `IRequestExceptionAction<TRequest>` | Side-effect on exception (no recovery) |

## Zero-Reflection Dispatch (Source Generator)

Register the source-generated dispatch table to eliminate all reflection on the hot path:

```csharp
builder.Services.AddHelix(typeof(Program).Assembly).UseHelixCodeGen();
```

Helix automatically falls back to reflection for any request type not covered by the generated table.

## License

MIT
