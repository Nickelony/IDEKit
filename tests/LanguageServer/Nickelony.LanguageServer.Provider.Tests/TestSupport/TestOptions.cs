namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Provides provider options that pin the path comparison, so a test chooses the policy instead of following the platform.</summary>
internal static class TestOptions
{
	internal static LanguageServerFeatureProviderOptions CaseSensitive { get; } =
		LanguageServerFeatureProviderOptions.Default with
		{
			PathComparison = LocalPathComparisonPolicy.CaseSensitive,
		};

	internal static LanguageServerFeatureProviderOptions CaseInsensitive { get; } =
		LanguageServerFeatureProviderOptions.Default with
		{
			PathComparison = LocalPathComparisonPolicy.CaseInsensitive,
		};
}
