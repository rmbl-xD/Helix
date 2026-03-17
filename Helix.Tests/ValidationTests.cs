using Microsoft.Extensions.DependencyInjection;

namespace Helix.Tests;

public class ValidationTests
{
    [Fact]
    public async Task Send_Command_WithPassingValidator_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(ValidationTests).Assembly);
        var provider = services.BuildServiceProvider();

        var helix = provider.GetRequiredService<IHelix>();
        var tracker = provider.GetRequiredService<CallTracker>();

        await helix.SendCommand(new TestValidatedCommand("ORD-001", 5));

        Assert.Contains("ValidatedHandler:ORD-001", tracker.Calls);
    }

    [Fact]
    public async Task Send_Command_WithFailingValidator_ThrowsValidationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(ValidationTests).Assembly);
        var provider = services.BuildServiceProvider();

        var helix = provider.GetRequiredService<IHelix>();

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => helix.SendCommand(new TestValidatedCommand("", -1)));

        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e.PropertyName == "OrderId");
        Assert.Contains(ex.Errors, e => e.PropertyName == "Quantity");
    }

    [Fact]
    public async Task Send_Command_WithFailingValidator_DoesNotInvokeHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CallTracker>();
        services.AddHelix(typeof(ValidationTests).Assembly);
        var provider = services.BuildServiceProvider();

        var helix = provider.GetRequiredService<IHelix>();
        var tracker = provider.GetRequiredService<CallTracker>();

        try
        {
            await helix.SendCommand(new TestValidatedCommand("", -1));
        }
        catch (ValidationException)
        {
            // expected
        }

        Assert.DoesNotContain(tracker.Calls, c => c.StartsWith("ValidatedHandler:"));
    }

    // ── Sample types ──

    public record TestValidatedCommand(string OrderId, int Quantity) : ICommand;

    public class TestValidatedCommandHandler(CallTracker tracker) : CommandHandler<TestValidatedCommand>
    {
        protected override Task Handle(TestValidatedCommand command, CancellationToken cancellationToken)
        {
            tracker.Track($"ValidatedHandler:{command.OrderId}");
            return Task.CompletedTask;
        }
    }

    public class TestValidatedCommandValidator : ICommandValidator<TestValidatedCommand>
    {
        public Task<ValidationResult> Validate(TestValidatedCommand command, CancellationToken cancellationToken = default)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(command.OrderId))
                result.Errors.Add(new ValidationFailure("OrderId", "OrderId is required."));

            if (command.Quantity <= 0)
                result.Errors.Add(new ValidationFailure("Quantity", "Quantity must be positive."));

            return Task.FromResult(result);
        }
    }
}
