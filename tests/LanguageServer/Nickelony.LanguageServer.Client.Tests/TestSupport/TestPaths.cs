namespace Nickelony.LanguageServer.Client.Tests;

/// <summary>Provides the synthetic absolute paths and file URIs that client tests use as fixtures.</summary>
/// <remarks>
/// Each path sits in the temporary folder, so it is fully qualified on every platform. A fixture is a deterministic path string, not a directory;
/// nothing here touches the file system.
/// </remarks>
internal static class TestPaths
{
	/// <summary>The synthetic workspace root, whose folder name is <c>Workspace</c>.</summary>
	internal static string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClientTestFixture", "Workspace"));

	/// <summary>A second workspace root that is not nested under <see cref="Root"/>, whose folder name is <c>Secondary</c>.</summary>
	internal static string SecondaryRoot { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ClientTestFixture", "Secondary"));

	/// <summary>Returns the path of <paramref name="fileName"/> inside the synthetic <c>Scripts</c> folder of <see cref="Root"/>.</summary>
	/// <param name="fileName">The file name, for example <c>first.ext</c>.</param>
	/// <returns>An absolute path below <see cref="Root"/>.</returns>
	internal static string Script(string fileName) => Path.Combine(Root, "Scripts", fileName);

	/// <summary>Returns the file URI of <paramref name="fileName"/> directly inside <see cref="Root"/>.</summary>
	/// <param name="fileName">The file name, for example <c>test.ext</c>.</param>
	/// <returns>The URI that <see cref="DocumentUriConversion.CreateFileUri"/> builds for the file.</returns>
	internal static string FileUri(string fileName) => DocumentUriConversion.CreateFileUri(Path.Combine(Root, fileName));

	/// <summary>Spells <paramref name="path"/> with the alternate directory separator and a trailing separator.</summary>
	/// <param name="path">The fully qualified path to respell.</param>
	/// <returns>
	/// A path that normalizes to <paramref name="path"/>; where the platform has one separator, only the trailing separator differs.
	/// </returns>
	internal static string WithAlternateSeparators(string path) =>
		path.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar;
}
