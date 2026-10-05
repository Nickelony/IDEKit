using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.Testing;

/// <summary>Provides a minimal typed definition discriminator for the hover and definition tests.</summary>
/// <param name="Kind">The discriminator kind that identifies the navigation target.</param>
internal sealed record TestDiscriminator(string Kind) : DefinitionDiscriminator;
