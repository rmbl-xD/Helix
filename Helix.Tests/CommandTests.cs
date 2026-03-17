using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class CommandTests
{
    private readonly IHelix _helix;
    private readonly CallTracker _tracker;

    public CommandTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(CommandTests).Assembly);
        var provider = services.BuildServiceProvider();

        _helix = provider.GetRequiredService<IHelix>();
        _tracker = provider.GetRequiredService<CallTracker>();
    }

    [Fact]
    public async Task Send_Command_InvokesHandler()
    {
        await _helix.Send(new TestCreateOrderCommand("ORD-001", 3));

        Assert.Contains("CreateOrderHandler:ORD-001:3", _tracker.Calls);
    }

    [Fact]
    public async Task SendCommand_TypedDispatch_InvokesHandler()
    {
        await _helix.SendCommand(new TestCreateOrderCommand("ORD-002", 5));

        Assert.Contains("CreateOrderHandler:ORD-002:5", _tracker.Calls);
    }

    [Fact]
    public async Task SendCommand_WithResponse_ReturnsResult()
    {
        var result = await _helix.SendCommand(new TestCreateOrderWithIdCommand("ORD-003", 7));

        Assert.Equal("ORD-003", result);
    }

    // ── Sample types ──

    public record TestCreateOrderCommand(string OrderId, int Quantity) : ICommand;

    public class TestCreateOrderCommandHandler(CallTracker tracker) : CommandHandler<TestCreateOrderCommand>
    {
        protected override Task Handle(TestCreateOrderCommand command, CancellationToken cancellationToken)
        {
            tracker.Track($"CreateOrderHandler:{command.OrderId}:{command.Quantity}");
            return Task.CompletedTask;
        }
    }

    public record TestCreateOrderWithIdCommand(string OrderId, int Quantity) : ICommand<string>;

    public class TestCreateOrderWithIdCommandHandler(CallTracker tracker) : ICommandHandler<TestCreateOrderWithIdCommand, string>
    {
        public Task<string> Handle(TestCreateOrderWithIdCommand request, CancellationToken cancellationToken)
        {
            tracker.Track($"CreateOrderWithIdHandler:{request.OrderId}");
            return Task.FromResult(request.OrderId);
        }
    }
}
