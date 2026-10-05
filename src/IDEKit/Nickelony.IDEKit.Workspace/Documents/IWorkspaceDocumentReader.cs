using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Provides read access to tracked workspace documents.</summary>
/// <remarks>
/// Consumers that only inspect document state can depend on this interface instead of the full <see cref="IWorkspaceDocumentStore"/>.
/// Snapshot lookups are point-in-time and do not wait for in-flight operations. A <see langword="null"/>, blank, or unnormalizable path
/// is invalid input, which a lookup reports as not tracked instead of throwing.
/// </remarks>
public interface IWorkspaceDocumentReader
{
	/// <summary>The workspace options the document identities are resolved with.</summary>
	/// <remarks>
	/// Consumers that track the same documents, such as a reload coordinator, should use this value so a customized path comparison cannot drift
	/// between the store and its consumers.
	/// </remarks>
	WorkspaceOptions Options { get; }

	/// <summary>The tracked document snapshots at or below a directory.</summary>
	/// <param name="directoryPath">
	/// The directory path to inspect; a <see langword="null"/>, blank, or unnormalizable path returns an empty list.
	/// </param>
	/// <returns>The tracked snapshots at or below the directory ordered by normalized document id; otherwise, an empty list.</returns>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	/// <remarks>
	/// A document tracked at the exact directory path is included, matching the subtree operations: a directory rename or delete reaches it too.
	/// </remarks>
	IReadOnlyList<WorkspaceDocumentSnapshot> GetSnapshotsUnderDirectory(string? directoryPath);

	/// <param name="filePath">The path to look up; a <see langword="null"/>, blank, or unnormalizable path reports <see langword="false"/>.</param>
	/// <param name="snapshot">The current snapshot when the path is tracked; otherwise, <see langword="null"/>.</param>
	/// <returns>
	/// <see langword="true"/> and the current snapshot when the path is tracked; otherwise, <see langword="false"/> and <see langword="null"/>.
	/// </returns>
	/// <exception cref="ObjectDisposedException">The store has been disposed.</exception>
	bool TryGetSnapshot(string? filePath, [NotNullWhen(true)] out WorkspaceDocumentSnapshot? snapshot);
}
