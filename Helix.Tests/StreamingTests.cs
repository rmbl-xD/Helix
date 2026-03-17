using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class StreamingTests
{
    private readonly IHelix _helix;

    public StreamingTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(StreamingTests).Assembly);
        var provider = services.BuildServiceProvider();

        _helix = provider.GetRequiredService<IHelix>();
    }

    [Fact]
    public async Task CreateStream_YieldsAllItems()
    {
        var items = new List<TestStreamOrderDto>();

        await foreach (var item in _helix.CreateStream(new TestGetAllOrdersStream()))
        {
            items.Add(item);
        }

        Assert.Equal(3, items.Count);
        Assert.Equal("ORD-001", items[0].Id);
        Assert.Equal(10, items[0].Quantity);
        Assert.Equal("ORD-002", items[1].Id);
        Assert.Equal(20, items[1].Quantity);
        Assert.Equal("ORD-003", items[2].Id);
        Assert.Equal(30, items[2].Quantity);
    }

    [Fact]
    public async Task CreateStream_StreamQuery_YieldsAllItems()
    {
        var items = new List<TestStreamOrderDto>();

        await foreach (var item in _helix.CreateStream(new TestGetRecentOrdersStreamQuery()))
        {
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        Assert.Equal("RECENT-001", items[0].Id);
        Assert.Equal("RECENT-002", items[1].Id);
    }

    // ── Sample types ──

    public record TestStreamOrderDto(string Id, int Quantity);

    public record TestGetAllOrdersStream() : IStreamRequest<TestStreamOrderDto>;

    public class TestGetAllOrdersStreamHandler : IStreamRequestHandler<TestGetAllOrdersStream, TestStreamOrderDto>
    {
        public async IAsyncEnumerable<TestStreamOrderDto> Handle(
            TestGetAllOrdersStream request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new TestStreamOrderDto("ORD-001", 10);
            await Task.Yield();
            yield return new TestStreamOrderDto("ORD-002", 20);
            await Task.Yield();
            yield return new TestStreamOrderDto("ORD-003", 30);
        }
    }

    // ── Stream Query types ──

    public record TestGetRecentOrdersStreamQuery() : IStreamQuery<TestStreamOrderDto>;

    public class TestGetRecentOrdersStreamQueryHandler : IStreamQueryHandler<TestGetRecentOrdersStreamQuery, TestStreamOrderDto>
    {
        public async IAsyncEnumerable<TestStreamOrderDto> Handle(
            TestGetRecentOrdersStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new TestStreamOrderDto("RECENT-001", 1);
            await Task.Yield();
            yield return new TestStreamOrderDto("RECENT-002", 2);
        }
    }
}
