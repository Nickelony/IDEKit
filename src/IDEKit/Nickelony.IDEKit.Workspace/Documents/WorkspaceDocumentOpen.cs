using System.Collections.ObjectModel;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Documents;

/// <summary>Describes the outcome of opening or loading a workspace document.</summary>
/// <remarks>
/// <see cref="AlreadyOpen"/> means the existing tracked snapshot was returned without another load.
/// </remarks>
public enum WorkspaceDocumentOpenOutcome
{
	/// <summary>The document was loaded from disk, or created as a new empty document when the path does not exist.</summary>
	Opened,

	AlreadyOpen,

	InvalidPath,

	/// <summary>
	/// The path does not exist and the open options did not allow creating a new document
	/// (<see cref="WorkspaceDocumentOpenOptions.CreateIfMissing"/> is <see langword="false"/>).
	/// </summary>
	NotFound,

	/// <summary>The path exists as a directory, not a file.</summary>
	/// <remarks>
	/// A directory cannot be tracked as a document: every later write or delete through the store would fail for it.
	/// </remarks>
	IsDirectory,

	LoadFailed,

	/// <summary>The operation was canceled, either by the caller's token or by the disposal of the store while the call was in flight.</summary>
	Canceled,
}

/// <summary>Specifies format defaults used when opening or creating a workspace document.</summary>
/// <param name="NoBomEncodings">The candidate encodings, in order of preference, for an existing file that has no byte-order mark.</param>
/// <param name="NewFileFormat">The format to use when the requested path does not exist.</param>
/// <param name="CreateIfMissing">
/// <see langword="true"/> (the default) to open a missing path as a new empty document;
/// <see langword="false"/> to report <see cref="WorkspaceDocumentOpenOutcome.NotFound"/> instead, which hosts that require an existing
/// file (build tools, servers) use to surface a missing file as an error rather than as new content.
/// </param>
/// <remarks>
/// The candidate list and the new-file format are validated where they are set, by the constructor or a <see langword="with"/> expression:
/// an empty or duplicated candidate list, or a new-file format that asks for a byte-order mark its encoding has none of, is an argument error
/// rather than a failure at the first open or commit. Options are not re-applied when the path is already tracked.
/// </remarks>
public sealed record WorkspaceDocumentOpenOptions(
	IReadOnlyList<TextEncoding> NoBomEncodings,
	TextFileFormat NewFileFormat,
	bool CreateIfMissing = true
)
{
	private readonly IReadOnlyList<TextEncoding> _noBomEncodings = ValidateNoBomEncodings(
		NoBomEncodings ?? throw new ArgumentNullException(nameof(NoBomEncodings))
	);

	private readonly TextFileFormat _newFileFormat = ValidateNewFileFormat(NewFileFormat);

	/// <summary>
	/// The open options a host uses when it has no more specific requirement: UTF-8 for a file without a byte-order mark, and a missing path opens
	/// as a new document in UTF-8 without a byte-order mark and with LF line terminators.
	/// </summary>
	public static WorkspaceDocumentOpenOptions Default { get; } =
		new([TextEncoding.Utf8], new TextFileFormat(TextEncoding.Utf8, HasBom: false, LineTerminatorStyle.Lf));

	/// <summary>The candidate encodings, in order of preference, for an existing file that has no byte-order mark.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The list is empty or lists an encoding twice.</exception>
	/// <remarks>
	/// The first candidate that decodes the file strictly wins, and the document records it in its format; the open fails
	/// with the <c>InvalidEncoding</c> failure code when none does. The library supplies only the presets of <see cref="TextEncoding"/>, so a host
	/// that wants another code page lists it here. The property holds a read-only copy of the list that was set.
	/// </remarks>
	public IReadOnlyList<TextEncoding> NoBomEncodings
	{
		get => _noBomEncodings;
		init => _noBomEncodings = ValidateNoBomEncodings(value ?? throw new ArgumentNullException(nameof(NoBomEncodings)));
	}

	/// <summary>The format to use when the requested path does not exist.</summary>
	/// <exception cref="ArgumentException">The format asks for a byte-order mark that its encoding has none of.</exception>
	public TextFileFormat NewFileFormat
	{
		get => _newFileFormat;
		init => _newFileFormat = ValidateNewFileFormat(value);
	}

	private static ReadOnlyCollection<TextEncoding> ValidateNoBomEncodings(IReadOnlyList<TextEncoding> value)
	{
		WorkspaceTextCodec.EnsureCandidates(value, nameof(NoBomEncodings));

		TextEncoding[] copy = [.. value];
		return Array.AsReadOnly(copy);
	}

	private static TextFileFormat ValidateNewFileFormat(TextFileFormat value)
	{
		WorkspaceTextCodec.EnsureEncodable(value, nameof(NewFileFormat));
		return value;
	}
}

/// <summary>Contains the outcome of opening or loading a workspace document.</summary>
/// <param name="Outcome">The open outcome.</param>
/// <param name="Snapshot">The loaded or already-open snapshot; otherwise, <see langword="null"/>.</param>
/// <param name="Failure">Explains a load failure, when one occurred.</param>
/// <remarks>
/// Only <see cref="WorkspaceDocumentOpenOutcome.Opened"/> and <see cref="WorkspaceDocumentOpenOutcome.AlreadyOpen"/> carry a snapshot.
/// </remarks>
public sealed record WorkspaceDocumentOpenResult(
	WorkspaceDocumentOpenOutcome Outcome,
	WorkspaceDocumentSnapshot? Snapshot,
	WorkspaceOperationFailure? Failure = null
);
