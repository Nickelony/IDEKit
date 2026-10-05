using System.Collections.Concurrent;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Remembers the documents whose close arrived while a temporary request lease still kept them tracked.</summary>
/// <param name="options">
/// The provider options whose path comparison decides which paths are one document, or <see langword="null"/> for the defaults.
/// </param>
/// <remarks>
/// <para>
/// The marker lets the lease's release complete the close, so the server copy does not stay open indefinitely. Each member is safe
/// on its own, but a claim and the re-mark that follows it are two calls, so a caller runs them inside the document's scheduler slot to keep
/// other flows for the same path out between them.
/// </para>
/// <para>
/// Paths are compared under the path comparison of the provider options.
/// </para>
/// </remarks>
internal sealed class PendingCloseTracker(LanguageServerFeatureProviderOptions? options = null)
{
	private readonly ConcurrentDictionary<string, byte> _pendingPaths = new(LanguageServerFeatureProviderOptions.PathComparisonOf(options).Comparer);

	/// <summary>Marks the document's close as pending until a request release completes it.</summary>
	/// <param name="filePath">The normalized document path.</param>
	internal void MarkPending(string filePath) => _pendingPaths[filePath] = 0;

	/// <summary>Determines whether a close is pending for the document.</summary>
	/// <param name="filePath">The normalized document path.</param>
	internal bool IsPending(string filePath) => _pendingPaths.ContainsKey(filePath);

	/// <summary>Forgets a pending close because the document was opened again, trimmed, or closed on its own.</summary>
	/// <param name="filePath">The normalized document path.</param>
	internal void Cancel(string filePath) => _pendingPaths.TryRemove(filePath, out _);

	/// <summary>Takes the pending close of a document so the caller can complete it.</summary>
	/// <param name="filePath">The normalized document path.</param>
	/// <returns><see langword="true"/> when a close was pending and is now the caller's to complete; otherwise, <see langword="false"/>.</returns>
	/// <remarks>
	/// A caller that cannot complete the close yet marks it pending again.
	/// </remarks>
	internal bool TryClaim(string filePath) => _pendingPaths.TryRemove(filePath, out _);

	/// <summary>Renames a pending close to the document's new path, so the eventual request release still completes it.</summary>
	/// <param name="oldFilePath">The normalized path the document left.</param>
	/// <param name="newFilePath">The normalized path the document took.</param>
	internal void Rename(string oldFilePath, string newFilePath)
	{
		if (_pendingPaths.TryRemove(oldFilePath, out _))
			_pendingPaths[newFilePath] = 0;
	}
}
