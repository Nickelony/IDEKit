using System.Windows;

namespace Nickelony.Testing;

/// <summary>A non-activating test host window that closes itself when disposed.</summary>
internal sealed class HostWindow : Window, IDisposable
{
	/// <inheritdoc/>
	public void Dispose()
	{
		if (IsVisible || IsLoaded)
			Close();
	}
}
