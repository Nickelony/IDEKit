using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

// The shared outcome of a document read: the read result, a cancellation, or a failure the caller
// maps onto its own result shape. The reload and conflict-resolution paths read the same way and
// differ only in how they map these states, so the read and its failure mapping live in StampedFileWorker.
internal readonly struct DocumentReadResult
{
	private DocumentReadResult(WorkspaceFileReadResult? file, WorkspaceOperationFailure? failure, bool isCanceled)
	{
		File = file;
		Failure = failure;
		IsCanceled = isCanceled;
	}

	// The read result when the file was read; otherwise, null.
	internal WorkspaceFileReadResult? File { get; }

	// The failure to report when the read failed; otherwise, null.
	internal WorkspaceOperationFailure? Failure { get; }

	// Indicates whether the file was read, which makes File available.
	[MemberNotNullWhen(true, nameof(File))]
	internal bool HasFile => File is not null;

	internal bool IsCanceled { get; }

	internal static DocumentReadResult Read(WorkspaceFileReadResult file) => new(file, failure: null, isCanceled: false);

	internal static DocumentReadResult Failed(WorkspaceOperationFailure failure) => new(file: null, failure, isCanceled: false);

	internal static DocumentReadResult Canceled() => new(file: null, failure: null, isCanceled: true);
}
