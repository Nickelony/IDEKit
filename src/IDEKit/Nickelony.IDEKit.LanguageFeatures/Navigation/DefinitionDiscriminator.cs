namespace Nickelony.IDEKit.LanguageFeatures.Navigation;

/// <summary>Identifies the language-specific kind of a definition lookup carried across provider boundaries.</summary>
/// <remarks>
/// Language packages derive from this type to carry a strongly typed discriminator from hover info or outline data into the discriminator
/// of a definition request. Recognition is type-based: a provider that does not recognize the discriminator it receives returns
/// <see langword="null"/> instead of falling back to the symbol name alone. A derived record must carry immutable, value-equal members to preserve
/// structural equality.
/// </remarks>
public abstract record DefinitionDiscriminator;
