using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Represents the confirmed before and after content and format of a document target.
/// </summary>
/// <remarks>
/// The validating init accessors follow the canonical construction pattern documented on
/// <see cref="WorkspaceEditTargetPreparation" />.
/// </remarks>
public sealed record WorkspaceDocumentChange
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _targetId = string.Empty;
	private readonly string _beforeContent = string.Empty;
	private readonly string _afterContent = string.Empty;

	/// <summary>
	/// Gets the stable target identifier of the changed document (typically its file path).
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string TargetId
	{
		get => _targetId;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_targetId = value;
		}
	}

	/// <summary>
	/// Gets the document content before the requested transformation.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string BeforeContent
	{
		get => _beforeContent;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_beforeContent = value;
		}
	}

	/// <summary>
	/// Gets the requested document content after the transformation.
	/// </summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string AfterContent
	{
		get => _afterContent;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_afterContent = value;
		}
	}

	/// <summary>
	/// Gets the document format before the transformation, when the producing path tracks it.
	/// </summary>
	/// <remarks>
	/// The workspace-edit applier records the prepared formats; a producer that applies changes
	/// without format knowledge (for example a host-side application path) leaves both format
	/// values <see langword="null"/>.
	/// </remarks>
	public TextFileFormat? BeforeFileFormat { get; init; }

	/// <summary>
	/// Gets the document format after the transformation, when the producing path tracks it.
	/// </summary>
	public TextFileFormat? AfterFileFormat { get; init; }
}
