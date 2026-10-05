using Nickelony.IDEKit.Core.Paths;
using Nickelony.LanguageServer.Provider;

namespace Nickelony.LanguageServer.Testing;

/// <summary>Provides the synthetic absolute paths used as test fixtures.</summary>
/// <remarks>
/// A fixture is a deterministic path string, not a directory; nothing here touches the file system.
/// </remarks>
internal static class TestPaths
{
	/// <summary>The synthetic workspace root.</summary>
	internal static string Root { get; } = Path.Combine(Path.GetTempPath(), "ProviderTestFixture");

	/// <summary>
	/// A second synthetic workspace root that is not nested under <see cref="Root"/>. The two directory names are deliberately unrelated
	/// so neither is a string prefix of the other.
	/// </summary>
	internal static string SecondaryRoot { get; } = Path.Combine(Path.GetTempPath(), "ProviderSecondaryFixture");

	/// <summary>A path that no file system can hold, because it contains a NUL character, so the provider cannot map it to a file.</summary>
	internal static string InvalidFile { get; } = Path.Combine(Root, "bad\0name.test");

	/// <summary>The path comparison of the provider defaults, which the collaborator tests build their inputs under.</summary>
	internal static LocalPathComparisonPolicy PathComparison { get; } = LanguageServerFeatureProviderOptions.Default.PathComparison;

	/// <summary>Returns the path of <paramref name="fileName"/> inside the synthetic <c>Scripts</c> folder of <see cref="Root"/>.</summary>
	/// <param name="fileName">The file name, for example <c>test.test</c>.</param>
	/// <returns>An absolute path below <see cref="Root"/>.</returns>
	internal static string Script(string fileName) => Path.Combine(Root, "Scripts", fileName);
}
