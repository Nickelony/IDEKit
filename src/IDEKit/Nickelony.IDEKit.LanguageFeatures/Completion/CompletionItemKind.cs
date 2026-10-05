namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Identifies the semantic category of a completion item for icon and styling purposes.</summary>
/// <remarks>
/// The well-known members are the shared vocabulary. A member with a protocol <c>CompletionItemKind</c> identifier
/// mirrors it one-to-one, and a member without one, such as <see cref="Namespace"/>, is never produced from a protocol value. The type is open,
/// so <see cref="CreateCustom(string)"/> adds a category whose identifier keeps its exact casing, and <see cref="FromIdentifier(string)"/> resolves
/// a well-known name to its category. The protocol bridge lives in <c>Nickelony.LanguageServer.Provider</c>, so this package stays protocol-free;
/// <see cref="Generic"/> is the fallback, not an LSP kind.
/// </remarks>
public sealed class CompletionItemKind : IEquatable<CompletionItemKind>
{
	/// <summary>The presentation fallback for a completion item whose producer cannot supply a category.</summary>
	/// <remarks>
	/// This is not an LSP kind. An unrecognized protocol value maps to this member at the protocol boundary, and a producer that cannot supply
	/// a category uses it directly.
	/// </remarks>
	public static CompletionItemKind Generic { get; } = new("Generic");

	/// <summary>A plain text completion item with no structured semantic category.</summary>
	public static CompletionItemKind Text { get; } = new("Text");

	public static CompletionItemKind Method { get; } = new("Method");

	/// <summary>A free or standalone function.</summary>
	public static CompletionItemKind Function { get; } = new("Function");

	public static CompletionItemKind Constructor { get; } = new("Constructor");

	public static CompletionItemKind Field { get; } = new("Field");

	public static CompletionItemKind Variable { get; } = new("Variable");

	public static CompletionItemKind Class { get; } = new("Class");

	public static CompletionItemKind Interface { get; } = new("Interface");

	public static CompletionItemKind Module { get; } = new("Module");

	public static CompletionItemKind Property { get; } = new("Property");

	/// <summary>A translation unit or compilation unit.</summary>
	public static CompletionItemKind Unit { get; } = new("Unit");

	public static CompletionItemKind Value { get; } = new("Value");

	public static CompletionItemKind Enum { get; } = new("Enum");

	public static CompletionItemKind Keyword { get; } = new("Keyword");

	public static CompletionItemKind Snippet { get; } = new("Snippet");

	public static CompletionItemKind Color { get; } = new("Color");

	public static CompletionItemKind File { get; } = new("File");

	public static CompletionItemKind Reference { get; } = new("Reference");

	public static CompletionItemKind Folder { get; } = new("Folder");

	public static CompletionItemKind EnumMember { get; } = new("EnumMember");

	public static CompletionItemKind Constant { get; } = new("Constant");

	public static CompletionItemKind Struct { get; } = new("Struct");

	public static CompletionItemKind Event { get; } = new("Event");

	public static CompletionItemKind Operator { get; } = new("Operator");

	public static CompletionItemKind TypeParameter { get; } = new("TypeParameter");

	public static CompletionItemKind Array { get; } = new("Array");

	public static CompletionItemKind Section { get; } = new("Section");

	public static CompletionItemKind Directive { get; } = new("Directive");

	public static CompletionItemKind Parameter { get; } = new("Parameter");

	public static CompletionItemKind Namespace { get; } = new("Namespace");

	private static readonly Dictionary<string, CompletionItemKind> s_wellKnownKinds = new(StringComparer.OrdinalIgnoreCase)
	{
		[Generic.Identifier] = Generic,
		[Text.Identifier] = Text,
		[Method.Identifier] = Method,
		[Function.Identifier] = Function,
		[Constructor.Identifier] = Constructor,
		[Field.Identifier] = Field,
		[Variable.Identifier] = Variable,
		[Class.Identifier] = Class,
		[Interface.Identifier] = Interface,
		[Module.Identifier] = Module,
		[Property.Identifier] = Property,
		[Unit.Identifier] = Unit,
		[Value.Identifier] = Value,
		[Enum.Identifier] = Enum,
		[Keyword.Identifier] = Keyword,
		[Snippet.Identifier] = Snippet,
		[Color.Identifier] = Color,
		[File.Identifier] = File,
		[Reference.Identifier] = Reference,
		[Folder.Identifier] = Folder,
		[EnumMember.Identifier] = EnumMember,
		[Constant.Identifier] = Constant,
		[Struct.Identifier] = Struct,
		[Event.Identifier] = Event,
		[Operator.Identifier] = Operator,
		[TypeParameter.Identifier] = TypeParameter,
		[Array.Identifier] = Array,
		[Section.Identifier] = Section,
		[Directive.Identifier] = Directive,
		[Parameter.Identifier] = Parameter,
		[Namespace.Identifier] = Namespace,
	};

	private CompletionItemKind(string identifier) => Identifier = identifier;

	/// <summary>The stable serialized identifier for this completion category.</summary>
	public string Identifier { get; }

	/// <summary>
	/// Creates a custom completion category.
	/// </summary>
	/// <param name="identifier">
	/// The custom identifier. After trimming, it must not be blank and must not match a well-known
	/// category case-insensitively.
	/// </param>
	/// <returns>
	/// An immutable custom completion category; repeated calls with the same identifier produce
	/// equal (but distinct) instances.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="identifier"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The identifier is blank or reserved for a well-known category.</exception>
	/// <example>
	/// <code>
	/// CompletionItemKind kind = CompletionItemKind.CreateCustom("vendor.type");
	/// </code>
	/// </example>
	public static CompletionItemKind CreateCustom(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		if (s_wellKnownKinds.ContainsKey(normalizedIdentifier))
			throw new ArgumentException($"The completion-kind identifier '{normalizedIdentifier}' is reserved.", nameof(identifier));

		return new CompletionItemKind(normalizedIdentifier);
	}

	/// <summary>Creates a category from a serialized identifier, retaining unknown identifiers as custom categories.</summary>
	/// <param name="identifier">The well-known or custom identifier.</param>
	/// <returns>The matching well-known category or a custom category.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">The identifier is blank.</exception>
	/// <remarks>
	/// A blank identifier is rejected with <see cref="ArgumentException"/> instead of being retained.
	/// Well-known identifiers are matched case-insensitively, so a canonical protocol name with a different casing resolves
	/// to the well-known category; any other identifier becomes a custom category.
	/// </remarks>
	public static CompletionItemKind FromIdentifier(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		return s_wellKnownKinds.TryGetValue(normalizedIdentifier, out CompletionItemKind? wellKnownKind)
			? wellKnownKind
			: new CompletionItemKind(normalizedIdentifier);
	}

	/// <inheritdoc/>
	public bool Equals(CompletionItemKind? other) => other is not null && StringComparer.Ordinal.Equals(Identifier, other.Identifier);

	/// <inheritdoc/>
	public override bool Equals(object? obj) => obj is CompletionItemKind other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identifier);

	/// <inheritdoc/>
	public override string ToString() => Identifier;

	/// <summary>Compares completion categories by their normalized ordinal identifiers.</summary>
	/// <param name="left">The left category, or <see langword="null"/>.</param>
	/// <param name="right">The right category, or <see langword="null"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the categories are equal, including two <see langword="null"/> values; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool operator ==(CompletionItemKind? left, CompletionItemKind? right) =>
		ReferenceEquals(left, right) || (left is not null && left.Equals(right));

	/// <summary>Compares completion categories by their normalized ordinal identifiers.</summary>
	/// <param name="left">The left category, or <see langword="null"/>.</param>
	/// <param name="right">The right category, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the categories differ; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(CompletionItemKind? left, CompletionItemKind? right) => !(left == right);
}
