using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class NotificationTests
{
    private readonly IHelix _helix;
    private readonly CallTracker _tracker;

    public NotificationTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(NotificationTests).Assembly);
        var provider = services.BuildServiceProvider();

        _helix = provider.GetRequiredService<IHelix>();
        _tracker = provider.GetRequiredService<CallTracker>();
    }

    [Fact]
    public async Task Publish_Notification_InvokesAllHandlers()
    {
        await _helix.Publish(new TestOrderCreatedEvent("ORD-001"));

        Assert.Contains("EmailHandler:ORD-001", _tracker.Calls);
        Assert.Contains("AnalyticsHandler:ORD-001", _tracker.Calls);
    }

    [Fact]
    public async Task Publish_DomainEvent_InvokesAllHandlers()
    {
        await _helix.Publish(new TestOrderShippedDomainEvent("ORD-002"));

        Assert.Contains("ShippingHandler:ORD-002", _tracker.Calls);
    }

    // ── Sample types (Notifications) ──

    public record TestOrderCreatedEvent(string OrderId) : INotification;

    public class TestOrderCreatedEmailHandler(CallTracker tracker) : INotificationHandler<TestOrderCreatedEvent>
    {
        public Task Handle(TestOrderCreatedEvent notification, CancellationToken cancellationToken)
        {
            tracker.Track($"EmailHandler:{notification.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class TestOrderCreatedAnalyticsHandler(CallTracker tracker) : INotificationHandler<TestOrderCreatedEvent>
    {
        public Task Handle(TestOrderCreatedEvent notification, CancellationToken cancellationToken)
        {
            tracker.Track($"AnalyticsHandler:{notification.OrderId}");
            return Task.CompletedTask;
        }
    }

    // ── Sample types (Domain Events) ──

    public record TestOrderShippedDomainEvent(string OrderId) : IDomainEvent
    {
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
    }

    public class TestOrderShippedHandler(CallTracker tracker) : IDomainEventHandler<TestOrderShippedDomainEvent>
    {
        public Task Handle(TestOrderShippedDomainEvent notification, CancellationToken cancellationToken)
        {
            tracker.Track($"ShippingHandler:{notification.OrderId}");
            return Task.CompletedTask;
        }
    }
}
