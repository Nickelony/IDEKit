using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Answers every definition request with one location and keeps the last request.</summary>
/// <param name="location">The location to answer with, or <see langword="null"/> for no definition.</param>
internal sealed class RecordingDefinitionProvider(DefinitionLocation? location) : IDefinitionProvider
{
	public DefinitionRequest? LastRequest { get; private set; }

	public Task<DefinitionLocation?> GetDefinitionAsync(DefinitionRequest request, CancellationToken cancellationToken = default)
	{
		LastRequest = request;
		return Task.FromResult(location);
	}
}
