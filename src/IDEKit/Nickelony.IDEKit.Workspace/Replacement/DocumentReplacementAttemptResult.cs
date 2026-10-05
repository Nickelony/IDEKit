using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>
/// Contains what one replacement of a <see cref="DocumentReplacementApplier"/> delegate produced: the document authority's result, or the reason
/// the delegate refused before it reached the document.
/// </summary>
/// <remarks>
/// A refused target is not applied, and its <see cref="DocumentReplacementTargetResult.Failure"/> is <see cref="Failure"/>.
/// </remarks>
public sealed class DocumentReplacementAttemptResult
{
	private DocumentReplacementAttemptResult(WorkspaceDocumentMutationResult? storeResult, WorkspaceOperationFailure? failure)
	{
		StoreResult = storeResult;
		Failure = failure;
	}

	/// <summary>The document authority's result, or <see langword="null"/> when the delegate refused.</summary>
	public WorkspaceDocumentMutationResult? StoreResult { get; }

	/// <summary>Why the delegate refused, or <see langword="null"/> when it reached the document.</summary>
	public WorkspaceOperationFailure? Failure { get; }

	/// <summary>Creates the result of a replacement that reached the document authority.</summary>
	/// <param name="storeResult">The document authority's result.</param>
	/// <exception cref="ArgumentNullException"><paramref name="storeResult"/> is <see langword="null"/>.</exception>
	public static DocumentReplacementAttemptResult Reached(WorkspaceDocumentMutationResult storeResult)
	{
		ArgumentNullException.ThrowIfNull(storeResult);
		return new(storeResult, failure: null);
	}

	/// <summary>Creates the result of a replacement the delegate refused before it reached the document.</summary>
	/// <param name="failure">Why the delegate refused; its code comes from the package that refused.</param>
	/// <exception cref="ArgumentNullException"><paramref name="failure"/> is <see langword="null"/>.</exception>
	public static DocumentReplacementAttemptResult Refused(WorkspaceOperationFailure failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		return new(storeResult: null, failure);
	}
}
