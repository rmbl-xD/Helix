namespace Helix;

/// <summary>
/// Represents a domain event — something that already happened in the domain.
/// Domain events are a specialization of <see cref="INotification"/> used in CQRS architectures
/// to propagate state changes across bounded contexts or trigger side effects.
/// </summary>
public interface IDomainEvent : INotification
{
    /// <summary>
    /// The point in time when the domain event occurred.
    /// </summary>
    DateTime OccurredOn { get; }
}

/// <summary>
/// Defines a handler for a domain event. Multiple handlers can be registered per event type.
/// Handlers execute sequentially through the standard notification pipeline.
/// </summary>
/// <typeparam name="TEvent">The domain event type.</typeparam>
public interface IDomainEventHandler<in TEvent> : INotificationHandler<TEvent>
    where TEvent : IDomainEvent;
