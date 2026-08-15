namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Identifies the semantic category of a completion item for icon and styling purposes.
/// </summary>
/// <remarks>
/// <para>
/// Well-known categories name the completion categories that language providers and editor hosts
/// commonly share.
/// The well-known set is the shared vocabulary, but the type itself is open: providers and hosts
/// may define additional categories with <see cref="CreateCustom(string)"/> without changing this
/// assembly.
/// </para>
/// <para>
/// The well-known members cover every LSP 3.17 <c>CompletionItemKind</c> value (1-25) one-to-one,
/// with identifiers that match the protocol names, so a language provider maps protocol kinds
/// without collapsing several values onto one category. The protocol bridge lives with the protocol
/// boundary in <c>Nickelony.LanguageServer.Client</c>, so this package stays protocol-free.
/// </para>
/// <para>
/// <see cref="Generic"/> is the presentation fallback for an item whose producer cannot supply a
/// category; it is not an LSP kind. <see cref="Array"/>, <see cref="Section"/>, <see cref="Directive"/>,
/// <see cref="Parameter"/>, and <see cref="Namespace"/> are library extensions without an LSP
/// counterpart.
/// </para>
/// <para>
/// Identifiers are normalized by trimming surrounding whitespace, and a blank identifier is
/// rejected. Well-known identifiers are reserved: <see cref="CreateCustom(string)"/> rejects them,
/// and <see cref="FromIdentifier(string)"/> resolves them case-insensitively, so a canonical name
/// with a different casing resolves to the well-known category instead of a spurious custom one. A
/// custom identifier keeps its exact casing. Category equality and hash codes compare the
/// normalized identifier ordinally, so custom categories need no registry. A renderer that does not
/// recognize a custom identifier should use <see cref="Generic"/> presentation while preserving the
/// original <see cref="Identifier"/> value in the model.
/// </para>
/// </remarks>
public sealed class TextCompletionItemKind : IEquatable<TextCompletionItemKind>
{
	/// <summary>
	/// The presentation fallback for a completion item whose producer cannot supply a category.
	/// </summary>
	/// <remarks>
	/// This is not an LSP kind. An unrecognized protocol value maps to this member at the protocol
	/// boundary, and a producer that cannot supply a category uses it directly.
	/// </remarks>
	public static TextCompletionItemKind Generic { get; } = new("Generic");

	/// <summary>
	/// A plain text completion item with no structured semantic category.
	/// </summary>
	public static TextCompletionItemKind Text { get; } = new("Text");

	/// <summary>
	/// A callable method or function.
	/// </summary>
	public static TextCompletionItemKind Method { get; } = new("Method");

	/// <summary>
	/// A free or standalone function.
	/// </summary>
	public static TextCompletionItemKind Function { get; } = new("Function");

	/// <summary>
	/// A constructor.
	/// </summary>
	public static TextCompletionItemKind Constructor { get; } = new("Constructor");

	/// <summary>
	/// A field.
	/// </summary>
	public static TextCompletionItemKind Field { get; } = new("Field");

	/// <summary>
	/// A variable.
	/// </summary>
	public static TextCompletionItemKind Variable { get; } = new("Variable");

	/// <summary>
	/// A class or object type.
	/// </summary>
	public static TextCompletionItemKind Class { get; } = new("Class");

	/// <summary>
	/// An interface or protocol.
	/// </summary>
	public static TextCompletionItemKind Interface { get; } = new("Interface");

	/// <summary>
	/// A module or package-like scope.
	/// </summary>
	public static TextCompletionItemKind Module { get; } = new("Module");

	/// <summary>
	/// A property.
	/// </summary>
	public static TextCompletionItemKind Property { get; } = new("Property");

	/// <summary>
	/// A translation unit or compilation unit.
	/// </summary>
	public static TextCompletionItemKind Unit { get; } = new("Unit");

	/// <summary>
	/// A value.
	/// </summary>
	public static TextCompletionItemKind Value { get; } = new("Value");

	/// <summary>
	/// An enumeration.
	/// </summary>
	public static TextCompletionItemKind Enum { get; } = new("Enum");

	/// <summary>
	/// A language keyword.
	/// </summary>
	public static TextCompletionItemKind Keyword { get; } = new("Keyword");

	/// <summary>
	/// A code snippet or template.
	/// </summary>
	public static TextCompletionItemKind Snippet { get; } = new("Snippet");

	/// <summary>
	/// A color value.
	/// </summary>
	public static TextCompletionItemKind Color { get; } = new("Color");

	/// <summary>
	/// A file system file.
	/// </summary>
	public static TextCompletionItemKind File { get; } = new("File");

	/// <summary>
	/// A reference to another symbol.
	/// </summary>
	public static TextCompletionItemKind Reference { get; } = new("Reference");

	/// <summary>
	/// A file system folder.
	/// </summary>
	public static TextCompletionItemKind Folder { get; } = new("Folder");

	/// <summary>
	/// A member of an enumeration.
	/// </summary>
	public static TextCompletionItemKind EnumMember { get; } = new("EnumMember");

	/// <summary>
	/// A constant value.
	/// </summary>
	public static TextCompletionItemKind Constant { get; } = new("Constant");

	/// <summary>
	/// A structure or value type.
	/// </summary>
	public static TextCompletionItemKind Struct { get; } = new("Struct");

	/// <summary>
	/// An event.
	/// </summary>
	public static TextCompletionItemKind Event { get; } = new("Event");

	/// <summary>
	/// An operator or overloaded operator.
	/// </summary>
	public static TextCompletionItemKind Operator { get; } = new("Operator");

	/// <summary>
	/// A type parameter or generic variable.
	/// </summary>
	public static TextCompletionItemKind TypeParameter { get; } = new("TypeParameter");

	/// <summary>
	/// An array-like value or container.
	/// </summary>
	public static TextCompletionItemKind Array { get; } = new("Array");

	/// <summary>
	/// A section header or named block.
	/// </summary>
	public static TextCompletionItemKind Section { get; } = new("Section");

	/// <summary>
	/// A directive or pragma-style keyword.
	/// </summary>
	public static TextCompletionItemKind Directive { get; } = new("Directive");

	/// <summary>
	/// A parameter.
	/// </summary>
	public static TextCompletionItemKind Parameter { get; } = new("Parameter");

	/// <summary>
	/// A namespace scope.
	/// </summary>
	public static TextCompletionItemKind Namespace { get; } = new("Namespace");

	private static readonly Dictionary<string, TextCompletionItemKind> s_wellKnownKinds =
		new(StringComparer.OrdinalIgnoreCase)
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
			[Namespace.Identifier] = Namespace
		};

	private TextCompletionItemKind(string identifier)
	{
		Identifier = identifier;
	}

	/// <summary>
	/// Gets the stable serialized identifier for this completion category.
	/// </summary>
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
	/// TextCompletionItemKind kind = TextCompletionItemKind.CreateCustom("vendor.type");
	/// </code>
	/// </example>
	public static TextCompletionItemKind CreateCustom(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		if (s_wellKnownKinds.ContainsKey(normalizedIdentifier))
		{
			throw new ArgumentException(
				$"The completion-kind identifier '{normalizedIdentifier}' is reserved.",
				nameof(identifier));
		}

		return new TextCompletionItemKind(normalizedIdentifier);
	}

	/// <summary>
	/// Creates a category from a serialized identifier, retaining unknown identifiers as custom
	/// categories.
	/// </summary>
	/// <remarks>
	/// A blank identifier is rejected with <see cref="ArgumentException"/> instead of being
	/// retained. Well-known identifiers are matched case-insensitively, so a canonical protocol name
	/// with a different casing resolves to the well-known category; any other identifier becomes a
	/// custom category.
	/// </remarks>
	/// <param name="identifier">The well-known or custom identifier.</param>
	/// <returns>The matching well-known category or a custom category.</returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="identifier"/> is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">The identifier is blank.</exception>
	public static TextCompletionItemKind FromIdentifier(string identifier)
	{
		string normalizedIdentifier = IdentifierVocabulary.Normalize(identifier);

		return s_wellKnownKinds.TryGetValue(normalizedIdentifier, out TextCompletionItemKind? wellKnownKind)
			? wellKnownKind
			: new TextCompletionItemKind(normalizedIdentifier);
	}

	/// <inheritdoc/>
	public bool Equals(TextCompletionItemKind? other)
		=> other is not null && StringComparer.Ordinal.Equals(Identifier, other.Identifier);

	/// <inheritdoc/>
	public override bool Equals(object? obj)
		=> obj is TextCompletionItemKind other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode()
		=> StringComparer.Ordinal.GetHashCode(Identifier);

	/// <inheritdoc/>
	public override string ToString() => Identifier;

	/// <summary>
	/// Compares completion categories by their normalized ordinal identifiers.
	/// </summary>
	/// <param name="left">The left category, or <see langword="null"/>.</param>
	/// <param name="right">The right category, or <see langword="null"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the categories are equal, including two <see langword="null"/>
	/// values; otherwise, <see langword="false"/>.
	/// </returns>
	public static bool operator ==(TextCompletionItemKind? left, TextCompletionItemKind? right)
		=> ReferenceEquals(left, right) || left is not null && left.Equals(right);

	/// <summary>
	/// Compares completion categories by their normalized ordinal identifiers.
	/// </summary>
	/// <param name="left">The left category, or <see langword="null"/>.</param>
	/// <param name="right">The right category, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> when the categories differ; otherwise, <see langword="false"/>.</returns>
	public static bool operator !=(TextCompletionItemKind? left, TextCompletionItemKind? right)
		=> !(left == right);
}
