using Nickelony.IDEKit.IntelliSense.Navigation;

namespace Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

/// <summary>
/// Provides a minimal typed definition discriminator for the hover and definition tests.
/// </summary>
/// <param name="Kind">The discriminator kind that identifies the navigation target.</param>
internal sealed record TestDiscriminator(string Kind) : TextDefinitionDiscriminator;
