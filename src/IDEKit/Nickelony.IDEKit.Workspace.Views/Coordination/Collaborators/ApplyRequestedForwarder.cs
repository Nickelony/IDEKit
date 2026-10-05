namespace Nickelony.IDEKit.Workspace.Views.Coordination;

/// <summary>
/// The per-registration forwarder that passes a view's <see cref="IWorkspaceDocumentView.ApplyRequested"/> event on to the coordinator.
/// </summary>
/// <remarks>
/// Delegate equality compares the target and the method, so every registration owns a distinct <see cref="Handler"/>
/// delegate and the multicast <c>-=</c> removes exactly the entry that registration added. A shared method-group handler cannot be told apart:
/// <c>-=</c> drops the last matching entry, so when the same view is detached and re-attached while an earlier attach is still adding its handler,
/// one attach's undo could strip the other's live handler. The forwarder keeps every add matched to its own removal.
/// </remarks>
internal sealed class ApplyRequestedForwarder
{
	private readonly EventHandler<ApplyRequestedEventArgs> _onApplyRequested;

	/// <param name="onApplyRequested">The handler that receives every apply request the forwarder passes on.</param>
	internal ApplyRequestedForwarder(EventHandler<ApplyRequestedEventArgs> onApplyRequested)
	{
		_onApplyRequested = onApplyRequested;
		Handler = Forward;
	}

	/// <summary>The delegate that is added to and removed from the view's event.</summary>
	internal EventHandler<ApplyRequestedEventArgs> Handler { get; }

	private void Forward(object? sender, ApplyRequestedEventArgs eventArgs) => _onApplyRequested(sender, eventArgs);
}
