using Nickelony.IDEKit.Workspace.Documents;

namespace Nickelony.IDEKit.Workspace.Editing;

/// <summary>
/// Describes one prepared document replacement for <see cref="WorkspaceEditApplier"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every member is required and settable only through an object initializer, so same-typed values
/// (the string contents, the format values, and the ids) cannot be swapped by argument position. The
/// validating init accessors are the canonical construction pattern for this group.
/// </para>
/// <para>
/// <see cref="Identity"/> carries the document instance, normalized id, and preflight version through
/// the same <see cref="WorkspaceDocumentRequestIdentity"/> value the replacement requests use.
/// </para>
/// <para>
/// The before values are used for no-op detection and for the change record; the applier does not
/// independently verify that they still match the workspace document.
/// </para>
/// </remarks>
public sealed record WorkspaceEditTargetPreparation
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _targetId = string.Empty;
	private readonly string _beforeContent = string.Empty;
	private readonly string _afterContent = string.Empty;

	/// <summary>
	/// Gets the stable target identifier of the document, typically its file path.
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
	/// Gets the document instance, normalized id, and the version the preparation expects.
	/// </summary>
	public required WorkspaceDocumentRequestIdentity Identity { get; init; }

	/// <summary>
	/// Gets the content before the requested transformation.
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
	/// Gets the format before the requested transformation.
	/// </summary>
	public required TextFileFormat BeforeFileFormat { get; init; }

	/// <summary>
	/// Gets the requested content after the transformation.
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
	/// Gets the requested format after the transformation.
	/// </summary>
	public required TextFileFormat AfterFileFormat { get; init; }
}
