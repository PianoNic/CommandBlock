using System.Threading.Tasks;
using TUnit.Core;

namespace CommandBlock.Tests.E2E;

/// <summary>Session-wide hooks: boot the CommandBlock stack once, tear it down at the end.</summary>
public static class CommandBlockSessionHooks
{
    [Before(HookType.TestSession)]
    public static async Task StartStack()
    {
        // Set CommandBlock_SKIP_E2E=1 to run unit tests without bringing up the throwaway compose
        // stack (handy on machines without Docker or when iterating on pure C# tests).
        // E2E tests themselves still call CommandBlockStack.GetAsync() on demand, so they'll surface
        // the missing stack with their own failure.
        if (string.Equals(Environment.GetEnvironmentVariable("CommandBlock_SKIP_E2E"), "1", StringComparison.Ordinal))
            return;
        // Touch the lazy stack so first-test latency isn't surprising in the log.
        await CommandBlockStack.GetAsync();
    }

    [After(HookType.TestSession)]
    public static async Task StopStack()
    {
        await CommandBlockTestFixture.DisposeAsync();
        await CommandBlockStack.StopSharedAsync();
    }
}
