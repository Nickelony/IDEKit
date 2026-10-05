using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

/// <summary>Represents a symbol (outline entry) found in a document.</summary>
/// <remarks>
/// Ranges use zero-based UTF-16 offsets (<see cref="TextRange"/>) rather than line and character coordinates, and are stored as supplied:
/// the containment of <see cref="SelectionRange"/> in <see cref="Range"/> is not validated. <see cref="Children"/> is an owned snapshot
/// with no <see langword="null"/> elements; instances use reference equality, and <see cref="Data"/> carries an opaque host payload.
/// </remarks>
public sealed class DocumentSymbol
{
	private readonly ReadOnlyCollection<DocumentSymbol> _children;
	private string? _detail;

	/// <param name="name">The display name of the symbol.</param>
	/// <param name="kind">The semantic category of the symbol.</param>
	/// <param name="range">
	/// The zero-based full range of the symbol, or <see langword="null"/> to fall back to <paramref name="selectionRange"/>.
	/// </param>
	/// <param name="selectionRange">The zero-based range of the symbol name, or <see langword="null"/> when unknown.</param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined <see cref="DocumentSymbolKind"/> value.</exception>
	public DocumentSymbol(string name, DocumentSymbolKind kind, TextRange? range = null, TextRange? selectionRange = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(
				nameof(kind),
				kind,
				"The kind is not a defined symbol kind; default(DocumentSymbolKind) is not a valid kind."
			);
		}

		Name = name;
		Kind = kind;
		Range = range ?? selectionRange;
		SelectionRange = selectionRange;
		_children = CollectionSnapshots.Empty<DocumentSymbol>();
	}

	/// <summary>The display name of the symbol.</summary>
	public string Name { get; }

	/// <summary>The semantic category of the symbol.</summary>
	public DocumentSymbolKind Kind { get; }

	/// <summary>The zero-based range of the symbol name, or <see langword="null"/> when unknown.</summary>
	public TextRange? SelectionRange { get; }

	/// <summary>The zero-based full range of the symbol, falling back to <see cref="SelectionRange"/>.</summary>
	public TextRange? Range { get; }

	/// <summary>
	/// The nested symbols; a <see langword="null"/> or empty assignment means none, and a <see langword="null"/> element is rejected.
	/// </summary>
	/// <exception cref="ArgumentException">A <see langword="null"/> element was supplied.</exception>
	[AllowNull]
	public IReadOnlyList<DocumentSymbol> Children
	{
		get => _children;
		init => _children = PrepareChildren(value, nameof(Children));
	}

	private static ReadOnlyCollection<DocumentSymbol> PrepareChildren(IReadOnlyList<DocumentSymbol>? children, string paramName) =>
		CollectionSnapshots.Capture(children, "children", paramName);

	/// <summary>The optional short detail line shown beside the name; blank values are treated as absent and others are trimmed.</summary>
	public string? Detail
	{
		get => _detail;
		init => _detail = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>The opaque host-specific payload (for example a provider-defined navigation target), which a host casts to its own type.</summary>
	public object? Data { get; init; }
}
