using Nickelony.IDEKit.Workspace.Documents;
using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Describes one prepared document replacement for <see cref="DocumentReplacementApplier"/>.</summary>
/// <remarks>
/// Every member is required and init-only, so same-typed values cannot be swapped by argument position, and the string members reject
/// a <see langword="null"/> value in their init accessors. <see cref="Identity"/> carries the document instance, normalized id, and preflight version
/// through the same <see cref="WorkspaceDocumentRequestIdentity"/> value the replacement requests use, and its id is the one the applier
/// groups by and reports, so a result names the document as the store does. The before values drive no-op detection and the change record;
/// the applier does not verify they still match.
/// </remarks>
public sealed record DocumentReplacementTarget
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _beforeContent = string.Empty;
	private readonly string _afterContent = string.Empty;

	/// <summary>The document instance, normalized id, and the version the replacement expects.</summary>
	public required WorkspaceDocumentRequestIdentity Identity { get; init; }

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

	public required TextFileFormat BeforeFileFormat { get; init; }

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

	public required TextFileFormat AfterFileFormat { get; init; }
}
