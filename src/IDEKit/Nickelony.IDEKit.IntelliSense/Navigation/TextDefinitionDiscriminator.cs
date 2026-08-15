namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Identifies the language-specific kind of a definition lookup carried across provider boundaries.
/// </summary>
/// <remarks>
/// <para>
/// Language packages derive from this type to carry a strongly typed discriminator. The value
/// travels from hover info or outline data into <see cref="TextDefinitionRequest.Discriminator"/> and
/// back to the language provider that recognizes it. Recognition is type-based (pattern matching):
/// a host or provider that does not recognize a discriminator ignores it, and a provider that does
/// not recognize one returns <see langword="null"/> from the definition lookup.
/// </para>
/// <para>
/// The base type is a record, so derived discriminators receive structural equality; a derived
/// discriminator must therefore carry immutable, value-equal members so two equivalent
/// discriminators compare equal.
/// </para>
/// </remarks>
public abstract record TextDefinitionDiscriminator;
