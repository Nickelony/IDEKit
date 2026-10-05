using Nickelony.IDEKit.Workspace.FileSystem;

namespace Nickelony.IDEKit.Workspace.Replacement;

/// <summary>Represents the confirmed before and after content and format of a document target.</summary>
public sealed record DocumentReplacementChange
{
	// The initializers only satisfy nullable analysis; every construction path assigns the fields
	// through the validating init accessors below.
	private readonly string _documentId = string.Empty;
	private readonly string _beforeContent = string.Empty;
	private readonly string _afterContent = string.Empty;

	/// <summary>The stable identifier of the changed document, typically its file path.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
	public required string DocumentId
	{
		get => _documentId;
		init
		{
			ArgumentNullException.ThrowIfNull(value);
			_documentId = value;
		}
	}

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

	/// <summary>The document format before the transformation, or <see langword="null"/> when the producing path does not track it.</summary>
	public TextFileFormat? BeforeFileFormat { get; init; }

	/// <summary>The document format after the transformation, or <see langword="null"/> when the producing path does not track it.</summary>
	public TextFileFormat? AfterFileFormat { get; init; }
}
