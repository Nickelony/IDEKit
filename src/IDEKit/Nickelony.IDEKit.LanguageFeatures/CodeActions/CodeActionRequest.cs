using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.IDEKit.LanguageFeatures.CodeActions;

/// <summary>Describes a code-action request against an immutable document snapshot.</summary>
/// <remarks>
/// <para>
/// The request addresses the document range the provider is asked about with zero-based UTF-16 offsets; a provider that needs
/// additional contextual state resolves it from <see cref="DocumentText"/> and the range itself. A host that derives the range from editor
/// state (for example the caret's diagnostic span) owns that mapping before constructing the request, and orders and clamps a selection first
/// because the request rejects a reversed or out-of-range one.
/// </para>
/// <para>
/// <see cref="Diagnostics"/> and <see cref="Only"/> are optional context that a host sets with an object initializer. Record equality compares
/// both lists by reference.
/// </para>
/// </remarks>
public sealed record CodeActionRequest
{
	private readonly IReadOnlyList<Diagnostic>? _diagnostics;
	private readonly IReadOnlyList<string>? _only;

	/// <param name="documentId">
	/// The identifier of the document the text belongs to. A language-server provider maps it to the document's file URI, and a local provider
	/// may ignore it.
	/// </param>
	/// <param name="documentText">The document text snapshot the range refers to.</param>
	/// <param name="range">The requested range.</param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="documentId"/> or <paramref name="documentText"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException"><paramref name="documentId"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="range"/> extends beyond the end of the document text.</exception>
	public CodeActionRequest(string documentId, string documentText, TextRange range)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
		ArgumentNullException.ThrowIfNull(documentText);

		TextOffsetValidation.ValidateRange(textLength: documentText.Length, range, parameterName: nameof(range), rangeDescription: "request");

		DocumentId = documentId;
		DocumentText = documentText;
		Range = range;
	}

	/// <summary>The identifier of the document the text belongs to.</summary>
	public string DocumentId { get; }

	/// <summary>The document text snapshot the range refers to.</summary>
	public string DocumentText { get; }

	/// <summary>The requested range.</summary>
	public TextRange Range { get; }

	/// <summary>
	/// The diagnostics the host reports for the requested range, whose ranges refer to <see cref="DocumentText"/>,
	/// or <see langword="null"/> when the host reports none and leaves the provider to use the diagnostics it knows.
	/// </summary>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> element.</exception>
	/// <exception cref="ArgumentOutOfRangeException">The range of an assigned diagnostic extends beyond the end of the document text.</exception>
	/// <remarks>
	/// An empty list states that no diagnostic belongs to the range, which differs from <see langword="null"/>. The setter copies the list.
	/// </remarks>
	public IReadOnlyList<Diagnostic>? Diagnostics
	{
		get => _diagnostics;
		init
		{
			if (value is null)
			{
				_diagnostics = null;
				return;
			}

			CollectionSnapshots.ValidateElements(value, "diagnostics", nameof(Diagnostics));

			foreach (Diagnostic diagnostic in value)
			{
				TextOffsetValidation.ValidateRange(
					textLength: DocumentText.Length,
					range: diagnostic.Range,
					parameterName: nameof(Diagnostics),
					rangeDescription: "diagnostic"
				);
			}

			_diagnostics = Array.AsReadOnly([.. value]);
		}
	}

	/// <summary>
	/// The action kinds the host asks for (for example <c>quickfix</c> or <c>refactor</c>), or <see langword="null"/> to ask for every kind.
	/// </summary>
	/// <exception cref="ArgumentException">The assigned list contains a <see langword="null"/> or blank element.</exception>
	/// <remarks>
	/// A kind selects itself and the kinds below it, so <c>refactor</c> also selects <c>refactor.extract</c>. The provider forwards
	/// the kinds to the server, which decides which actions it offers. The setter copies the list and trims the surrounding whitespace of each kind,
	/// as <see cref="CodeAction.Kind"/> does.
	/// </remarks>
	public IReadOnlyList<string>? Only
	{
		get => _only;
		init
		{
			if (value is null)
			{
				_only = null;
				return;
			}

			List<string> kinds = [];

			foreach (string kind in value)
			{
				if (string.IsNullOrWhiteSpace(kind))
					throw new ArgumentException("The kinds contain a null or blank element.", nameof(Only));

				kinds.Add(kind.Trim());
			}

			_only = kinds.AsReadOnly();
		}
	}
}
