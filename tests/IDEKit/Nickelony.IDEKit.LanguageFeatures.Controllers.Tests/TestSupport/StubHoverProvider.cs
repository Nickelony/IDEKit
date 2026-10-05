using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Answers every hover request with one fixed result.</summary>
/// <param name="hoverInfo">The hover result to answer with, or <see langword="null"/> for no hover.</param>
internal sealed class StubHoverProvider(HoverInformation? hoverInfo) : IHoverProvider
{
	public Task<HoverInformation?> GetHoverAsync(HoverRequest request, CancellationToken cancellationToken = default) => Task.FromResult(hoverInfo);
}
