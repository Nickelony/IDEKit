namespace Nickelony.IDEKit.FileWatching.Tests;

/// <summary>Provides the synthetic absolute paths used as test fixtures.</summary>
/// <remarks>
/// A fixture is a deterministic path string, not a directory; nothing here touches the file system.
/// </remarks>
internal static class TestPaths
{
	/// <summary>The synthetic workspace root.</summary>
	internal static string Root { get; } = Path.Combine(Path.GetTempPath(), "FileWatchingTestFixture");

	/// <summary>The path comparison that the tests build their inputs under.</summary>
	internal static LocalPathComparisonPolicy PathComparison { get; } = LocalPathComparisonPolicy.ForCurrentPlatform;

	/// <summary>Returns the path of <paramref name="fileName"/> inside the synthetic <c>Scripts</c> folder of <see cref="Root"/>.</summary>
	/// <param name="fileName">The file name, for example <c>test.test</c>.</param>
	/// <returns>An absolute path below <see cref="Root"/>.</returns>
	internal static string Script(string fileName) => Path.Combine(Root, "Scripts", fileName);
}
