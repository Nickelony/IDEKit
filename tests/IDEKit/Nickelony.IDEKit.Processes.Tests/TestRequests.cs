namespace Nickelony.IDEKit.Processes.Tests;

/// <summary>
/// Provides the base <see cref="ProcessRunRequest"/> for tests; a test derives its request from
/// <see cref="Default"/> with a <see langword="with"/> expression.
/// </summary>
internal static class TestRequests
{
	/// <summary>
	/// Gets a request that names a placeholder executable and keeps every optional property at its default.
	/// </summary>
	public static ProcessRunRequest Default { get; } = new()
	{
		FileName = "tool.exe",
	};
}
