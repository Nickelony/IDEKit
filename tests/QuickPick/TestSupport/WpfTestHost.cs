using System.Windows.Threading;

namespace Nickelony.QuickPick.Testing;

/// <summary>Adds the quick-pick controller context to the WPF hosting helpers of <see cref="WpfToolkitTestHost"/>.</summary>
internal sealed class WpfTestHost : WpfToolkitTestHost
{
	private WpfTestHost() { }

	/// <summary>
	/// Makes the WPF dispatcher context the current one of the calling thread, as a running application does, because a quick-pick controller takes
	/// it from there.
	/// </summary>
	public static void EnsureUiContext()
	{
		if (SynchronizationContext.Current is not DispatcherSynchronizationContext)
			SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
	}
}
