namespace Nickelony.QuickPick;

/// <summary>Tracks which provider previewed which item, so a session pairs every preview with one cancel or one accept.</summary>
/// <remarks>
/// A preview of the item that is already previewed is not repeated. A preview in another provider cancels the earlier one first, so two providers
/// never hold a change at once. The provider is called through containment, so a provider that throws does not stop the session.
/// </remarks>
internal sealed class QuickPickPreviewTracker
{
	private readonly Action<Exception> _reportFault;

	private IQuickPickProvider? _provider;
	private string? _key;

	/// <param name="reportFault">Reports a fault of a provider.</param>
	internal QuickPickPreviewTracker(Action<Exception> reportFault) => _reportFault = reportFault;

	/// <summary>Previews an item through its provider unless that item is already previewed.</summary>
	/// <param name="provider">The provider that listed the item.</param>
	/// <param name="item">The active item, which is enabled.</param>
	internal void Show(IQuickPickProvider provider, QuickPickItem item)
	{
		if (ReferenceEquals(_provider, provider) && _key == item.Key)
			return;

		if (_provider is not null && !ReferenceEquals(_provider, provider))
			Cancel();

		_provider = provider;
		_key = item.Key;

		CallbackContainment.TryRun(() => provider.Preview(item), _reportFault);
	}

	/// <summary>Asks the provider of the preview to restore what the preview changed, and forgets the preview.</summary>
	internal void Cancel()
	{
		IQuickPickProvider? provider = _provider;

		Forget();

		if (provider is not null)
			CallbackContainment.TryRun(provider.CancelPreview, _reportFault);
	}

	/// <summary>Forgets the preview without asking the provider to restore it, because an accept keeps what the preview changed.</summary>
	internal void Forget()
	{
		_provider = null;
		_key = null;
	}
}
