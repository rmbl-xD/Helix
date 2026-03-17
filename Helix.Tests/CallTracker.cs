namespace Helix.Tests;

/// <summary>
/// Tracks method calls in test handlers to enable assertions on execution order and invocation.
/// Register as singleton in the test DI container.
/// </summary>
public class CallTracker
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public void Track(string call) => _calls.Add(call);
}
