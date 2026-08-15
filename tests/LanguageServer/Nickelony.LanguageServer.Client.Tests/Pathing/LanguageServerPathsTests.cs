namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class LanguageServerPathsTests
{
	[TestMethod]
	public void CreateFileUri_AndTryGetLocalPath_RoundTripNormalizedPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "test file.ext"));
		string rawPath = expectedPath.Replace('\\', '/');

		string uri = LanguageServerPaths.CreateFileUri(rawPath);

		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithPercentSequenceInFileName_RoundTripsTheLiteralName()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a%41b.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		// The literal percent sequence must stay an escaped literal instead of decoding back to "aAb.txt".
		Assert.IsTrue(uri.Contains("a%2541b.txt", StringComparison.Ordinal));
		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void NormalizeLocalPath_TrimsTrailingDirectorySeparatorForNonRootPath()
	{
		string rawPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "Folder")) + Path.DirectorySeparatorChar;
		string normalizedPath = LanguageServerPaths.NormalizeLocalPath(rawPath);

		Assert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rawPath)), normalizedPath);
	}

	[TestMethod]
	public void PathIdentityMembers_FollowThePlatformRule()
	{
		bool expectedCaseSensitivity = !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

		// The platform decides and the package exposes no override, so every identity member derives from the
		// same rule and a host that needs a different one compares paths itself.
		Assert.AreEqual(expectedCaseSensitivity, LanguageServerPaths.UsesCaseSensitiveLocalPaths);
		Assert.AreEqual(
			expectedCaseSensitivity ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
			LanguageServerPaths.LocalPathComparison);
		Assert.AreEqual(
			expectedCaseSensitivity ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase,
			LanguageServerPaths.LocalPathComparer);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows | OperatingSystems.OSX)]
	public void AreLocalPathsEqual_OnCaseInsensitivePlatform_TreatsCasingVariantsAsEqual()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper"));
		string lowerCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "case.ext"));
		string upperCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "CASE.ext"));

		Assert.IsFalse(LanguageServerPaths.UsesCaseSensitiveLocalPaths);
		Assert.IsTrue(LanguageServerPaths.AreLocalPathsEqual(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux)]
	public void AreLocalPathsEqual_OnCaseSensitivePlatform_TreatsCasingVariantsAsDistinct()
	{
		string directoryPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper"));
		string lowerCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "case.ext"));
		string upperCasePath = LanguageServerPaths.NormalizeLocalPath(Path.Combine(directoryPath, "CASE.ext"));

		Assert.IsTrue(LanguageServerPaths.UsesCaseSensitiveLocalPaths);
		Assert.IsFalse(LanguageServerPaths.AreLocalPathsEqual(lowerCasePath, upperCasePath));
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_HandlesUncPath()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("The test pins Windows UNC path normalization.");

		Uri uri = new("file://server/share/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"\\server\share\folder\test.ext");

		string normalizedPath = LanguageServerPaths.NormalizeLocalPath(uri);

		Assert.AreEqual(expectedPath, normalizedPath);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_WithRemoteAuthority_IsRejectedWhenUncAuthorityIsUnsupported()
	{
		// A file URI authority cannot be represented as a local path on a host without UNC support: Uri.LocalPath
		// would drop it and collapse the URI onto the local path of the same shape. Pin the rejection on every
		// platform through the internal capability overload.
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerPaths.NormalizeLocalPath(
			new Uri("file://server/share/folder/test.ext"),
			supportsUncAuthority: false));
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_WithLocalAuthority_IsAcceptedWhenUncAuthorityIsUnsupported()
	{
		string normalizedPath = LanguageServerPaths.NormalizeLocalPath(
			new Uri("file://localhost/folder/test.ext"),
			supportsUncAuthority: false);

		Assert.IsFalse(string.IsNullOrWhiteSpace(normalizedPath));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public void NormalizeLocalPath_Uri_OnUnix_RejectsUriWithRemoteAuthority()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			LanguageServerPaths.NormalizeLocalPath(new Uri("file://server/share/folder/test.ext")));
	}

	[TestMethod]
	public void TryGetLocalPath_ReturnsFalseForNonFileUri()
	{
		Assert.IsFalse(LanguageServerPaths.TryGetLocalPath("https://example.com/test.ext", out string filePath));
		Assert.AreEqual(string.Empty, filePath);
	}

	[TestMethod]
	public void TryNormalizeLocalPath_ReturnsFalseForBlankInput()
	{
		Assert.IsFalse(LanguageServerPaths.TryNormalizeLocalPath(" ", out string normalizedPath));
		Assert.AreEqual(string.Empty, normalizedPath);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_RejectsNonFileUri()
	{
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerPaths.NormalizeLocalPath(new Uri("https://example.com/test.ext")));
		Assert.ThrowsExactly<ArgumentException>(() => LanguageServerPaths.NormalizeLocalPath(new Uri("relative/path.ext", UriKind.Relative)));
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_FileUriWithDriveLetter_ReturnsLocalPath()
	{
		if (!OperatingSystem.IsWindows())
			Assert.Inconclusive("The test pins Windows drive-letter URI normalization.");

		Uri uri = new("file:///C:/test/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"C:\test\folder\test.ext");

		Assert.AreEqual(expectedPath, LanguageServerPaths.NormalizeLocalPath(uri));
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_UsesAuthorityLessThreeSlashShape()
	{
		// A rooted POSIX path must not be concatenated verbatim: four slashes would parse as a UNC authority on
		// non-Windows hosts, which corrupts every document URI.
		Assert.AreEqual("file:///home/user/workspace/main.lua", LanguageServerPaths.BuildFileUri("/home/user/workspace/main.lua"));
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_IsNotParsedAsUncAuthority()
		=> Assert.AreEqual(string.Empty, new Uri(LanguageServerPaths.BuildFileUri("/home/user/x")).Host);

	[TestMethod]
	public void BuildFileUri_UncPath_BecomesTheUriAuthority()
	{
		Assert.AreEqual("file://server/share/dir/file.lua", LanguageServerPaths.BuildFileUri(@"\\server\share\dir\file.lua"));
	}

	[TestMethod]
	public void BuildFileUri_DrivePath_UsesThreeSlashes()
	{
		Assert.AreEqual("file:///C:/temp/a.txt", LanguageServerPaths.BuildFileUri(@"C:\temp\a.txt"));
	}

	[TestMethod]
	public void BuildFileUri_ExtendedLengthPrefixes_AreStrippedBeforeBuilding()
	{
		Assert.AreEqual("file:///C:/temp/a.txt", LanguageServerPaths.BuildFileUri(@"\\?\C:\temp\a.txt"));
		Assert.AreEqual("file://server/share/x.lua", LanguageServerPaths.BuildFileUri(@"\\?\UNC\server\share\x.lua"));
	}

	[TestMethod]
	public void BuildFileUri_EscapesPercentHashAndQuestionMarkCharacters()
	{
		Assert.AreEqual("file:///home/user/a%23b%3Fc%25d.txt", LanguageServerPaths.BuildFileUri("/home/user/a#b?c%d.txt"));
	}

	[TestMethod]
	public void CreateFileUri_WithHashAndQuestionMarkCharacters_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a#b?c.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		Assert.IsTrue(uri.Contains("a%23b%3Fc.txt", StringComparison.Ordinal), uri);
		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithNonAsciiFileNameFileName_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "日本語 test.txt"));

		string uri = LanguageServerPaths.CreateFileUri(expectedPath);

		Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_Rfc3986SpecialCharactersInFileName_RoundTripThroughTryGetLocalPath()
	{
		// The escaping policy must preserve every RFC 3986 unreserved character, every sub-delim, and the
		// characters that are ordinary in a file name but special in a URI: the URI is the document's identity,
		// so a round-trip that loses a character would silently retarget the document.
		string[] fileNames =
		[
			"a b.txt", "a!b.txt", "a$b.txt", "a&b.txt", "a'b.txt", "a(b).txt", "a*b.txt", "a+b.txt",
			"a,b.txt", "a;b.txt", "a=b.txt", "a@b.txt", "a~b.txt", "a-b_c.d.txt",
			"a[b].txt", "a{b}.txt", "a^b.txt", "a`b.txt", "a<b>.txt", "a\"b.txt", "a|b.txt", "a:b.txt"
		];

		foreach (string fileName in fileNames)
		{
			string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", fileName));

			string uri = LanguageServerPaths.CreateFileUri(expectedPath);

			Assert.IsTrue(LanguageServerPaths.TryGetLocalPath(uri, out string filePath), $"'{fileName}' produced '{uri}'.");
			Assert.AreEqual(expectedPath, filePath, $"'{fileName}' did not round-trip through '{uri}'.");
		}
	}

	[TestMethod]
	public void NormalizeWorkspaceRoots_EmptyList_ModelsAFolderlessSession()
	{
		Assert.AreEqual(0, LanguageServerPaths.NormalizeWorkspaceRoots([]).Length);
	}
}
