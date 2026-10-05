namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>The bounds that the tests of a real child process wait for it.</summary>
internal static class TestTimeouts
{
	/// <summary>How long a test waits for a real child process to exit, so an exit that never comes fails the test instead of hanging it.</summary>
	public const int WaitTimeoutMilliseconds = 10_000;
}
