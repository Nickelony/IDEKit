namespace Nickelony.QuickPick.Avalonia.Tests;

/// <summary>Adds the quick-pick controller context to the Avalonia headless hosting helpers of <see cref="AvaloniaToolkitTestHost"/>.</summary>
internal sealed class AvaloniaTestHost : AvaloniaToolkitTestHost
{
	private AvaloniaTestHost() { }

	/// <summary>Verifies that the calling thread is the Avalonia UI thread, whose current context a quick-pick controller takes.</summary>
	public static void EnsureUiContext()
	{
		if (SynchronizationContext.Current is null)
			throw new InvalidOperationException("The calling thread is not the Avalonia UI thread.");
	}
}
