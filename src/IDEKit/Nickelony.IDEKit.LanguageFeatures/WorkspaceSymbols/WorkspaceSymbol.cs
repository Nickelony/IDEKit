using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

/// <summary>Represents a symbol found anywhere in a workspace, with the document that declares it.</summary>
/// <remarks>
/// A workspace symbol is flat: it names the symbol that contains it through <see cref="ContainerName"/> and carries no children.
/// Instances use reference equality, and <see cref="Data"/> carries an opaque provider payload that <see cref="IWorkspaceSymbolResolver"/> reads.
/// </remarks>
public sealed class WorkspaceSymbol
{
	private string? _containerName;

	/// <param name="name">The display name of the symbol.</param>
	/// <param name="kind">The semantic category of the symbol.</param>
	/// <param name="location">Where the symbol is declared.</param>
	/// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="location"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException"><paramref name="name"/> is blank.</exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined <see cref="DocumentSymbolKind"/> value.</exception>
	public WorkspaceSymbol(string name, DocumentSymbolKind kind, WorkspaceSymbolLocation location)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(location);

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
		Location = location;
	}

	/// <summary>The display name of the symbol.</summary>
	public string Name { get; }

	/// <summary>The semantic category of the symbol, in the vocabulary of the document symbols.</summary>
	public DocumentSymbolKind Kind { get; }

	/// <summary>Where the symbol is declared.</summary>
	public WorkspaceSymbolLocation Location { get; }

	/// <summary>
	/// The name of the symbol that contains this one, such as its type or namespace; blank values are treated as absent and others are trimmed.
	/// </summary>
	public string? ContainerName
	{
		get => _containerName;
		init => _containerName = OptionalTextNormalizer.Normalize(value);
	}

	/// <summary>The opaque provider payload, which the provider reads again when it resolves the symbol.</summary>
	public object? Data { get; init; }
}
