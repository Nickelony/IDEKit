using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Views;

/// <summary>Holds a text view's publication back while an earlier one awaits its acknowledgment, and recognizes that acknowledgment.</summary>
/// <remarks>
/// A second publication raised before the first is acknowledged would carry the same base version, so the store would reject it and an acknowledgment
/// of the first could be taken for the second. The gate keeps one publication in flight and marks a republish instead.
/// </remarks>
internal sealed class PublicationGate
{
	private WorkspaceDocumentReplaceRequest? _inFlight;
	private bool _isRepublishMarked;

	/// <summary>Holds a publication back and marks a republish when one is in flight, and returns whether it held it back.</summary>
	internal bool TryHoldBack()
	{
		if (_inFlight is null)
			return false;

		_isRepublishMarked = true;
		return true;
	}

	/// <summary>Records the publication that is about to be raised.</summary>
	/// <param name="request">The replacement the view raises.</param>
	internal void Record(WorkspaceDocumentReplaceRequest request) => _inFlight = request;

	/// <summary>Determines whether <paramref name="request"/> is the publication in flight.</summary>
	/// <param name="request">The replacement to compare.</param>
	internal bool IsInFlight(WorkspaceDocumentReplaceRequest request) => ReferenceEquals(_inFlight, request);

	/// <summary>Determines whether a mutation result acknowledges the publication in flight.</summary>
	/// <param name="result">The mutation result the view acknowledges.</param>
	/// <param name="applied">Whether the store applied the mutation.</param>
	/// <remarks>
	/// An applied result must also carry the published text, because a discard requested against the same version is not this publication.
	/// </remarks>
	internal bool Acknowledges(WorkspaceDocumentMutationResult result, bool applied)
	{
		return _inFlight is { } inFlight
			&& inFlight.Identity == result.RequestedIdentity
			&& (!applied || string.Equals(result.Snapshot?.Content, inFlight.Content, StringComparison.Ordinal));
	}

	/// <summary>
	/// Ends the publication in flight, as its acknowledgment or a close does, and returns whether a republish was marked meanwhile.
	/// </summary>
	internal bool Complete()
	{
		bool isRepublishMarked = _isRepublishMarked;

		_inFlight = null;
		_isRepublishMarked = false;

		return isRepublishMarked;
	}
}
