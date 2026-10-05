using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Provides the replacement requested by a view.</summary>
/// <remarks>
/// The coordinator applies <see cref="Request"/> as a logical mutation; committing it to disk is a separate operation.
/// </remarks>
public sealed class ApplyRequestedEventArgs : EventArgs
{
	/// <param name="request">The replacement to apply.</param>
	/// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
	public ApplyRequestedEventArgs(WorkspaceDocumentReplaceRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		Request = request;
	}

	/// <summary>The replacement requested by the view.</summary>
	public WorkspaceDocumentReplaceRequest Request { get; }
}
