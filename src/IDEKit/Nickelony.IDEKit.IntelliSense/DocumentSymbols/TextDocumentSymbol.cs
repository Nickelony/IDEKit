using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.Infrastructure;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Nickelony.IDEKit.IntelliSense.DocumentSymbols;

/// <summary>
/// Represents a symbol (outline entry) found in a document.
/// </summary>
/// <remarks>
/// <para>
/// A neutral outline entry: ranges use zero-based UTF-16 offsets (<see cref="TextRange"/>) rather
/// than line and character coordinates, and the symbol kind is <see cref="TextDocumentSymbolKind"/>.
/// Nested symbols are carried in <see cref="Children"/>, and an optional host payload is available
/// through <see cref="Data"/>. Unlike the protocol's document symbol, both ranges are optional and
/// deprecation annotations are not modeled.
/// </para>
/// <para>
/// <see cref="Children"/> is an owned snapshot and never contains <see langword="null"/> elements.
/// Providers should keep <see cref="SelectionRange"/> contained in <see cref="Range"/>
/// when both are known; the library stores ranges as supplied and does not validate containment,
/// because providers may supply partial range data. Instances use reference equality.
/// </para>
/// </remarks>
public sealed class TextDocumentSymbol
{
	private readonly ReadOnlyCollection<TextDocumentSymbol> _children;
	private string? _detail;

	/// <summary>
	/// Initializes a new instance of the <see cref="TextDocumentSymbol"/> class. Optional values are
	/// assigned with an object initializer.
	/// </summary>
	/// <param name="name">The display name of the symbol.</param>
	/// <param name="kind">The semantic category of the symbol.</param>
	/// <param name="range">
	/// The zero-based full range of the symbol, or <see langword="null"/> to fall back to
	/// <paramref name="selectionRange"/>.
	/// </param>
	/// <param name="selectionRange">
	/// The zero-based range of the symbol name, or <see langword="null"/> when unknown.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="kind"/> is not a defined <see cref="TextDocumentSymbolKind"/> value.
	/// </exception>
	public TextDocumentSymbol(
		string name,
		TextDocumentSymbolKind kind,
		TextRange? range = null,
		TextRange? selectionRange = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(
				nameof(kind),
				kind,
				"The kind is not a defined symbol kind; default(TextDocumentSymbolKind) is not a valid kind.");
		}

		Name = name;
		Kind = kind;
		Range = range ?? selectionRange;
		SelectionRange = selectionRange;
		_children = PayloadCollections.Empty<TextDocumentSymbol>();
	}

	/// <summary>
	/// Gets the display name of the symbol.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the semantic category of the symbol.
	/// </summary>
	public TextDocumentSymbolKind Kind { get; }

	/// <summary>
	/// Gets the zero-based range of the symbol name, or <see langword="null"/> when unknown.
	/// </summary>
	public TextRange? SelectionRange { get; }

	/// <summary>
	/// Gets the zero-based full range of the symbol, falling back to <see cref="SelectionRange"/>.
	/// </summary>
	public TextRange? Range { get; }

	/// <summary>
	/// Gets the owned snapshot of nested symbols, assigned with an object initializer. A
	/// <see langword="null"/> or empty assignment means there are no children; the collection must
	/// not contain <see langword="null"/> elements.
	/// </summary>
	/// <exception cref="ArgumentException">A <see langword="null"/> element was supplied.</exception>
	[AllowNull]
	public IReadOnlyList<TextDocumentSymbol> Children
	{
		get => _children;
		init => _children = PrepareChildren(value, nameof(Children));
	}

	private static ReadOnlyCollection<TextDocumentSymbol> PrepareChildren(
		IReadOnlyList<TextDocumentSymbol>? children,
		string paramName)
	{
		return PayloadCollections.Capture(children, "children", paramName);
	}

	/// <summary>
	/// Gets the optional short detail line shown beside the name, assigned with an object initializer;
	/// blank values are treated as absent and other values are trimmed.
	/// </summary>
	public string? Detail
	{
		get => _detail;
		init => _detail = OptionalText.Normalize(value);
	}

	/// <summary>
	/// Gets the optional host-specific payload (for example a provider-defined navigation target),
	/// assigned with an object initializer. The payload is intentionally opaque; hosts cast it to
	/// their own type before use.
	/// </summary>
	public object? Data { get; init; }
}
