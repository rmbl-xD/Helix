using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class PipelineTests
{
    private readonly IHelix _helix;
    private readonly CallTracker _tracker;

    public PipelineTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(PipelineTests).Assembly);
        var provider = services.BuildServiceProvider();

        _helix = provider.GetRequiredService<IHelix>();
        _tracker = provider.GetRequiredService<CallTracker>();
    }

    [Fact]
    public async Task Send_ExecutesPipelineInOrder()
    {
        await _helix.Send(new TestPipelineCommand("ORD-001"));

        Assert.Equal(
        [
            "PreProcessor:ORD-001",
            "Behavior:Before:TestPipelineCommand",
            "Handler:ORD-001",
            "Behavior:After:TestPipelineCommand",
            "PostProcessor:ORD-001",
        ], _tracker.Calls.Where(c =>
            c.StartsWith("PreProcessor:ORD-001") ||
            c.StartsWith("Behavior:") && c.Contains("TestPipelineCommand") ||
            c.StartsWith("Handler:ORD-001") ||
            c.StartsWith("PostProcessor:ORD-001")).ToList());
    }

    [Fact]
    public async Task Send_PreProcessor_ExecutesBeforeHandler()
    {
        await _helix.Send(new TestPipelineCommand("ORD-002"));

        var calls = _tracker.Calls.ToList();
        var preCalls = calls.Where(c => c.StartsWith("PreProcessor:ORD-002")).ToList();
        var handlerCalls = calls.Where(c => c.StartsWith("Handler:ORD-002")).ToList();

        Assert.Single(preCalls);
        Assert.Single(handlerCalls);
        Assert.True(calls.IndexOf(preCalls[0]) < calls.IndexOf(handlerCalls[0]));
    }

    [Fact]
    public async Task Send_PostProcessor_ExecutesAfterHandler()
    {
        await _helix.Send(new TestPipelineCommand("ORD-003"));

        var calls = _tracker.Calls.ToList();
        var handlerCalls = calls.Where(c => c.StartsWith("Handler:ORD-003")).ToList();
        var postCalls = calls.Where(c => c.StartsWith("PostProcessor:ORD-003")).ToList();

        Assert.Single(handlerCalls);
        Assert.Single(postCalls);
        Assert.True(calls.IndexOf(handlerCalls[0]) < calls.IndexOf(postCalls[0]));
    }

    [Fact]
    public async Task Send_Behavior_WrapsHandler()
    {
        await _helix.Send(new TestPipelineCommand("ORD-004"));

        var relevant = _tracker.Calls.Where(c =>
            c.Contains("TestPipelineCommand") || c.StartsWith("Handler:ORD-004")).ToList();

        Assert.Contains("Behavior:Before:TestPipelineCommand", relevant);
        Assert.Contains("Handler:ORD-004", relevant);
        Assert.Contains("Behavior:After:TestPipelineCommand", relevant);

        var beforeIdx = relevant.IndexOf("Behavior:Before:TestPipelineCommand");
        var handlerIdx = relevant.IndexOf("Handler:ORD-004");
        var afterIdx = relevant.IndexOf("Behavior:After:TestPipelineCommand");

        Assert.True(beforeIdx < handlerIdx);
        Assert.True(handlerIdx < afterIdx);
    }

    // ── Sample types ──

    public record TestPipelineCommand(string OrderId) : ICommand;

    public class TestPipelineCommandHandler(CallTracker tracker) : CommandHandler<TestPipelineCommand>
    {
        protected override Task Handle(TestPipelineCommand command, CancellationToken cancellationToken)
        {
            tracker.Track($"Handler:{command.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class TestPipelinePreProcessor(CallTracker tracker) : IRequestPreProcessor<TestPipelineCommand>
    {
        public Task Process(TestPipelineCommand request, CancellationToken cancellationToken)
        {
            tracker.Track($"PreProcessor:{request.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class TestPipelinePostProcessor(CallTracker tracker) : IRequestPostProcessor<TestPipelineCommand, Unit>
    {
        public Task Process(TestPipelineCommand request, Unit response, CancellationToken cancellationToken)
        {
            tracker.Track($"PostProcessor:{request.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class TestPipelineBehavior(CallTracker tracker) : IPipelineBehavior<TestPipelineCommand, Unit>
    {
        public async Task<Unit> Handle(TestPipelineCommand request, RequestHandlerDelegate<Unit> next, CancellationToken cancellationToken)
        {
            tracker.Track($"Behavior:Before:{nameof(TestPipelineCommand)}");
            var response = await next();
            tracker.Track($"Behavior:After:{nameof(TestPipelineCommand)}");
            return response;
        }
    }
}
