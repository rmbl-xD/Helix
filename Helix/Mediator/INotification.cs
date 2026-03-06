namespace Helix;

/// <summary>
/// Marker interface for a notification that can be published to multiple handlers.
/// </summary>
public interface INotification;

/// <summary>
/// Defines a handler for a notification. Multiple handlers can be registered per notification type.
/// </summary>
/// <typeparam name="TNotification">The notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
