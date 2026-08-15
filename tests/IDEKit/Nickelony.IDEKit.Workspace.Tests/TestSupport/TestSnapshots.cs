using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Tests;

/// <summary>
/// Creates workspace document snapshots for tests that only need a well-formed snapshot.
/// </summary>
internal static class TestSnapshots
{
	/// <summary>
	/// Gets the file format shared by the test snapshots (UTF-8 without a byte-order mark, LF newlines).
	/// </summary>
	public static TextFileFormat FileFormat { get; } = new(
		TextEncodingKind.Utf8,
		false,
		TextNewlineStyle.Lf);

	/// <summary>
	/// Creates a clean snapshot whose id doubles as its display path and text-snapshot identifier.
	/// </summary>
	/// <param name="documentId">The normalized document id, also used as the display path.</param>
	/// <param name="content">The snapshot content.</param>
	/// <param name="version">The logical and persisted version; the default is zero.</param>
	/// <param name="documentKey">The document incarnation; a new one when omitted.</param>
	public static WorkspaceDocumentSnapshot Create(
		string documentId,
		string content,
		long version = 0,
		WorkspaceDocumentKey? documentKey = null)
		=> new(
			documentKey ?? new WorkspaceDocumentKey(Guid.NewGuid()),
			documentId,
			documentId,
			version,
			version,
			false,
			new StringTextSnapshot(content, documentId),
			FileFormat,
			FileStamp.Missing);
}
