using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class ExceptionHandlingTests
{
    private readonly IHelix _helix;
    private readonly CallTracker _tracker;

    public ExceptionHandlingTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(ExceptionHandlingTests).Assembly);
        var provider = services.BuildServiceProvider();

        _helix = provider.GetRequiredService<IHelix>();
        _tracker = provider.GetRequiredService<CallTracker>();
    }

    [Fact]
    public async Task Send_WithExceptionHandler_RecoversWithFallback()
    {
        var result = await _helix.Send(new TestFailingQuery());

        Assert.Equal("FALLBACK", result.Id);
        Assert.Equal(0, result.Quantity);
    }

    [Fact]
    public async Task Send_WithExceptionAction_ExecutesSideEffect()
    {
        await _helix.Send(new TestFailingQuery());

        Assert.Contains(_tracker.Calls, c => c.StartsWith("ExceptionAction:"));
    }

    [Fact]
    public async Task Send_WithExceptionHandler_ExceptionHandlerRunsBeforeAction()
    {
        await _helix.Send(new TestFailingQuery());

        var handlerCall = _tracker.Calls.FirstOrDefault(c => c.StartsWith("ExceptionHandler:"));
        var actionCall = _tracker.Calls.FirstOrDefault(c => c.StartsWith("ExceptionAction:"));

        Assert.NotNull(handlerCall);
        Assert.NotNull(actionCall);
        var calls = _tracker.Calls.ToList();
        Assert.True(calls.IndexOf(handlerCall) < calls.IndexOf(actionCall));
    }

    [Fact]
    public async Task Send_WithNoExceptionHandler_Throws()
    {
        var ex = await Assert.ThrowsAsync<System.Reflection.TargetInvocationException>(
            () => _helix.Send(new TestUnhandledFailingQuery()));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal("Unhandled failure!", ex.InnerException.Message);
    }

    // ── Sample types (handled exception) ──

    public record TestExceptionDto(string Id, int Quantity);

    public record TestFailingQuery() : IQuery<TestExceptionDto>;

    public class TestFailingQueryHandler : IQueryHandler<TestFailingQuery, TestExceptionDto>
    {
        public Task<TestExceptionDto> Handle(TestFailingQuery request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Something went wrong!");
        }
    }

    public class TestFailingQueryExceptionHandler(CallTracker tracker) : IRequestExceptionHandler<TestFailingQuery, TestExceptionDto>
    {
        public Task Handle(TestFailingQuery request, Exception exception, RequestExceptionHandlerState<TestExceptionDto> state, CancellationToken cancellationToken)
        {
            tracker.Track($"ExceptionHandler:{exception.Message}");
            state.SetHandled(new TestExceptionDto("FALLBACK", 0));
            return Task.CompletedTask;
        }
    }

    public class TestFailingQueryExceptionAction(CallTracker tracker) : IRequestExceptionAction<TestFailingQuery>
    {
        public Task Execute(TestFailingQuery request, Exception exception, CancellationToken cancellationToken)
        {
            tracker.Track($"ExceptionAction:{exception.Message}");
            return Task.CompletedTask;
        }
    }

    // ── Sample types (unhandled exception) ──

    public record TestUnhandledFailingQuery() : IQuery<TestExceptionDto>;

    public class TestUnhandledFailingQueryHandler : IQueryHandler<TestUnhandledFailingQuery, TestExceptionDto>
    {
        public Task<TestExceptionDto> Handle(TestUnhandledFailingQuery request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Unhandled failure!");
        }
    }
}
