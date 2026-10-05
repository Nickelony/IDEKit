using Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.DocumentSymbols;

/// <summary>Carries the choices of the document symbol modes: whether they group by kind, and the texts they show.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned.
/// </remarks>
public sealed record DocumentSymbolQuickPickOptions
{
	private Func<DocumentSymbolKind, string?> _groupLabel = DefaultGroupLabel;

	private string _name = "Go to symbol in editor";
	private string _groupedName = "Go to symbol in editor by category";
	private string _placeholder = "Type the name of a symbol to go to";
	private string _emptyText = "No matching editor symbols";
	private string _noDocumentText = "Open a file with symbols to go to a symbol in it.";
	private string _noSymbolsText = "The editor has no symbols.";

	/// <summary>The options with the default of every option.</summary>
	public static DocumentSymbolQuickPickOptions Default { get; } = new();

	/// <summary>Whether the mode groups the symbols by kind, as the mode <c>@:</c> does; defaults to <see langword="false"/>.</summary>
	/// <remarks>
	/// A mode that groups answers the prefix <c>@:</c> and lists a separator per kind, with the number of symbols of the kind, in the order
	/// of the separator texts. The other mode answers <c>@</c> and lists the symbols in document order under one separator.
	/// </remarks>
	public bool GroupByKind { get; init; }

	/// <summary>
	/// The text of the one separator above the symbols of the ungrouped mode, or <see langword="null"/> to draw none; defaults to "symbols".
	/// </summary>
	/// <remarks>
	/// The separator ends with the number of symbols on screen, such as <c>symbols (12)</c>.
	/// </remarks>
	public string? SymbolsLabel { get; init; } = "symbols";

	/// <summary>
	/// Gets the text of the separator above the symbols of one kind in the grouped mode; defaults to <see cref="DefaultGroupLabel"/>.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned function is <see langword="null"/>.</exception>
	/// <remarks>
	/// The separators are listed in the alphabetical order of their texts, as VS Code orders them, and the text ends with the number of symbols
	/// of the kind on screen. A <see langword="null"/> text draws no separator for the kind.
	/// </remarks>
	public Func<DocumentSymbolKind, string?> GroupLabel
	{
		get => _groupLabel;
		init => _groupLabel = value ?? throw new ArgumentNullException(nameof(GroupLabel));
	}

	/// <summary>The name of the ungrouped mode, which the list of modes shows; the default is "Go to symbol in editor".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Name
	{
		get => _name;
		init => _name = value ?? throw new ArgumentNullException(nameof(Name));
	}

	/// <summary>The name of the grouped mode; the default is "Go to symbol in editor by category".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string GroupedName
	{
		get => _groupedName;
		init => _groupedName = value ?? throw new ArgumentNullException(nameof(GroupedName));
	}

	/// <summary>The hint shown in the empty text box while the mode is active; the default is "Type the name of a symbol to go to".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The text of the row that a query without a match leaves; the default is "No matching editor symbols".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string EmptyText
	{
		get => _emptyText;
		init => _emptyText = value ?? throw new ArgumentNullException(nameof(EmptyText));
	}

	/// <summary>
	/// The text of the dimmed row that tells no document has an outline; the default is "Open a file with symbols to go to a symbol in it.".
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string NoDocumentText
	{
		get => _noDocumentText;
		init => _noDocumentText = value ?? throw new ArgumentNullException(nameof(NoDocumentText));
	}

	/// <summary>The text of the dimmed row that tells the document has no symbols; the default is "The editor has no symbols.".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string NoSymbolsText
	{
		get => _noSymbolsText;
		init => _noSymbolsText = value ?? throw new ArgumentNullException(nameof(NoSymbolsText));
	}

	/// <summary>Gets the English separator text that VS Code shows for a kind, such as "methods" or "type parameters".</summary>
	/// <param name="kind">The kind of the symbols.</param>
	/// <returns>The plural name of the kind.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined <see cref="DocumentSymbolKind"/> value.</exception>
	public static string DefaultGroupLabel(DocumentSymbolKind kind)
	{
		return kind switch
		{
			DocumentSymbolKind.File => "files",
			DocumentSymbolKind.Module => "modules",
			DocumentSymbolKind.Namespace => "namespaces",
			DocumentSymbolKind.Package => "packages",
			DocumentSymbolKind.Class => "classes",
			DocumentSymbolKind.Method => "methods",
			DocumentSymbolKind.Property => "properties",
			DocumentSymbolKind.Field => "fields",
			DocumentSymbolKind.Constructor => "constructors",
			DocumentSymbolKind.Enum => "enumerations",
			DocumentSymbolKind.Interface => "interfaces",
			DocumentSymbolKind.Function => "functions",
			DocumentSymbolKind.Variable => "variables",
			DocumentSymbolKind.Constant => "constants",
			DocumentSymbolKind.String => "strings",
			DocumentSymbolKind.Number => "numbers",
			DocumentSymbolKind.Boolean => "booleans",
			DocumentSymbolKind.Array => "arrays",
			DocumentSymbolKind.Object => "objects",
			DocumentSymbolKind.Key => "keys",
			DocumentSymbolKind.Null => "nulls",
			DocumentSymbolKind.EnumMember => "enumeration members",
			DocumentSymbolKind.Struct => "structs",
			DocumentSymbolKind.Event => "events",
			DocumentSymbolKind.Operator => "operators",
			DocumentSymbolKind.TypeParameter => "type parameters",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind is not a defined symbol kind."),
		};
	}
}
