using System.Text;

namespace Nickelony.IDEKit.Workspace;

/// <summary>Lists the stable failure codes produced by workspace operations.</summary>
/// <remarks>
/// <see cref="WorkspaceOperationFailure.Code"/> carries one of these values for document-authority failures. A custom file-system implementation
/// reports its own failures with the codes that describe its operation, reusing these spellings where they fit. The set can grow, so a host
/// that branches on a code keeps a default branch.
/// </remarks>
public static class WorkspaceOperationFailureCodes
{
	/// <summary>The document could not be loaded from disk when it was opened.</summary>
	public const string LoadFailed = "LoadFailed";

	public const string WriteFailed = "WriteFailed";

	/// <summary>The document could not be read from disk during a reload or conflict resolution.</summary>
	public const string ReadFailed = "ReadFailed";

	/// <summary>A path exists as a directory where the operation requires a file.</summary>
	public const string IsDirectory = "IsDirectory";

	/// <summary>A path exists as a file where the operation requires a directory.</summary>
	public const string NotDirectory = "NotDirectory";

	/// <summary>The disk file changed in a way that conflicts with the caller's expectation.</summary>
	public const string ExternalFileConflict = "ExternalFileConflict";

	/// <summary>The file content is not valid for the selected encoding.</summary>
	/// <remarks>
	/// A decode failure and an encode failure both report this code with the failing operation's own outcome, so a host can distinguish an encoding
	/// problem from an I/O problem.
	/// </remarks>
	public const string InvalidEncoding = "InvalidEncoding";

	public const string MoveFailed = "MoveFailed";

	/// <summary>
	/// The move failed and its final state could not be established; for example, a case-only move whose rollback move also failed.
	/// </summary>
	public const string MoveStateUnknown = "MoveStateUnknown";

	public const string DeleteFailed = "DeleteFailed";

	/// <summary>
	/// A file or directory could not be accessed because of a permission failure, reported by the read, write, move, and delete paths in place
	/// of the generic failure code.
	/// </summary>
	public const string AccessDenied = "AccessDenied";

	/// <summary>A move or save-as destination already exists.</summary>
	public const string DestinationExists = "DestinationExists";

	/// <summary>The replacement may have completed, but its final state could not be established.</summary>
	public const string ReplacementStateUnknown = "ReplacementStateUnknown";

	public const string Canceled = "Canceled";

	/// <summary>A reload asked for an encoding, but the file system returned decoded content, so there are no bytes to decode.</summary>
	/// <remarks>
	/// A custom file system that decodes inside its read must return the bytes for a document to be reopened with an encoding.
	/// </remarks>
	public const string EncodingNotApplicable = "EncodingNotApplicable";

	/// <summary>The process cannot resolve an encoding that the operation needs, typically because no code-page provider is registered.</summary>
	/// <remarks>
	/// An open, a reload, a conflict resolution and a save report it in place of <see cref="InvalidEncoding"/> when the encoding
	/// cannot be resolved at all. Registering the provider recovers, because nothing is cached for the failure.
	/// </remarks>
	public const string EncodingUnavailable = "EncodingUnavailable";

	// A permission failure is a property of the path, not of the read: every read path reports
	// AccessDenied for it like the write paths, so a host can tell an unfixable permission problem from
	// a transient read failure. A decode failure is an encoding problem, so open, reload and conflict
	// resolution agree on InvalidEncoding for the same root cause. Anything else takes the caller's
	// fallback code, which names the operation: LoadFailed for an open, ReadFailed for a reload.
	internal static string ClassifyReadFailure(Exception exception, string fallbackCode)
	{
		return exception switch
		{
			DecoderFallbackException => WorkspaceOperationFailureCodes.InvalidEncoding,
			EncodingUnavailableException => WorkspaceOperationFailureCodes.EncodingUnavailable,
			UnauthorizedAccessException => WorkspaceOperationFailureCodes.AccessDenied,
			_ => fallbackCode,
		};
	}

	// Encoding failures are a property of the content and the selected format, not a write problem:
	// the decode paths classify the equivalent condition as InvalidEncoding, so writes do too instead
	// of reporting an unfixable encoding mismatch as a generic write failure.
	internal static string ClassifyWriteFailure(Exception exception)
	{
		return exception switch
		{
			EncoderFallbackException => WorkspaceOperationFailureCodes.InvalidEncoding,
			EncodingUnavailableException => WorkspaceOperationFailureCodes.EncodingUnavailable,
			_ => WorkspaceOperationFailureCodes.WriteFailed,
		};
	}

	// A faulted directory operation reports an UnauthorizedAccessException as AccessDenied, like the
	// read paths; any other exception keeps the operation's own failure code.
	internal static string ClassifyDirectoryOperationFailure(Exception exception, string operationCode) =>
		exception is UnauthorizedAccessException ? WorkspaceOperationFailureCodes.AccessDenied : operationCode;
}
