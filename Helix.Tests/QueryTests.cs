using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class QueryTests
{
    private readonly IHelix _helix;
    private readonly IHelix _helixCodeGen;

    public QueryTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(QueryTests).Assembly);
        var provider = services.BuildServiceProvider();
        _helix = provider.GetRequiredService<IHelix>();

        var cgServices = new ServiceCollection();
        cgServices.AddSingleton<CallTracker>();
        cgServices.AddHelix(typeof(QueryTests).Assembly).UseHelixCodeGen();
        var cgProvider = cgServices.BuildServiceProvider();
        _helixCodeGen = cgProvider.GetRequiredService<IHelix>();
    }

    [Fact]
    public async Task Send_Query_ReturnsExpectedResult()
    {
        var result = await _helix.Send(new TestGetOrderQuery("ORD-001"));

        Assert.Equal("ORD-001", result.Id);
        Assert.Equal(42, result.Quantity);
    }

    [Fact]
    public async Task SendQuery_TypedDispatch_ReturnsExpectedResult()
    {
        var result = await _helix.SendQuery(new TestGetOrderQuery("ORD-002"));

        Assert.Equal("ORD-002", result.Id);
        Assert.Equal(42, result.Quantity);
    }

    [Fact]
    public async Task Send_Query_CodeGen_ReturnsExpectedResult()
    {
        var result = await _helixCodeGen.Send(new TestGetOrderQuery("ORD-003"));

        Assert.Equal("ORD-003", result.Id);
        Assert.Equal(42, result.Quantity);
    }

    // ── Sample types ──

    public record TestOrderDto(string Id, int Quantity);

    public record TestGetOrderQuery(string OrderId) : IQuery<TestOrderDto>;

    public class TestGetOrderQueryHandler : IQueryHandler<TestGetOrderQuery, TestOrderDto>
    {
        public Task<TestOrderDto> Handle(TestGetOrderQuery request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TestOrderDto(request.OrderId, 42));
        }
    }
}
