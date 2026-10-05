namespace Nickelony.LanguageServer.Client.Tests;

[TestClass]
public sealed class DocumentUriConversionTests
{
	[TestMethod]
	public void CreateFileUri_AndTryGetLocalPath_RoundTripsTheFullLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "test file.ext"));
		string rawPath = expectedPath.Replace('\\', '/');

		string uri = DocumentUriConversion.CreateFileUri(rawPath);

		Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithPercentSequenceInFileName_RoundTripsTheLiteralName()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a%41b.txt"));

		string uri = DocumentUriConversion.CreateFileUri(expectedPath);

		// The literal percent sequence must stay an escaped literal instead of decoding back to "aAb.txt".
		Assert.IsTrue(uri.Contains("a%2541b.txt", StringComparison.Ordinal));
		Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void NormalizeLocalPath_Uri_HandlesUncPath()
	{
		Uri uri = new("file://server/share/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"\\server\share\folder\test.ext");

		string normalizedPath = DocumentUriConversion.NormalizeLocalPath(uri, supportsUncAuthority: true);

		Assert.AreEqual(expectedPath, normalizedPath);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_WithRemoteAuthority_IsRejectedWhenUncAuthorityIsUnsupported()
	{
		// A file URI authority cannot be represented as a local path on a host without UNC support: Uri.LocalPath
		// would drop it and collapse the URI onto the local path of the same shape. Pin the rejection on every
		// platform through the internal capability overload.
		Assert.ThrowsExactly<ArgumentException>(() =>
			DocumentUriConversion.NormalizeLocalPath(new Uri("file://server/share/folder/test.ext"), supportsUncAuthority: false)
		);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_WithLocalAuthority_IsAcceptedWhenUncAuthorityIsUnsupported()
	{
		string normalizedPath = DocumentUriConversion.NormalizeLocalPath(new Uri("file://localhost/folder/test.ext"), supportsUncAuthority: false);
		Assert.IsFalse(string.IsNullOrWhiteSpace(normalizedPath));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Linux | OperatingSystems.OSX)]
	public void TryGetLocalPath_OnUnix_RejectsUriWithRemoteAuthority() =>
		Assert.IsFalse(DocumentUriConversion.TryGetLocalPath("file://server/share/folder/test.ext", out _));

	[TestMethod]
	public void TryGetLocalPath_ReturnsFalseForNonFileUri()
	{
		Assert.IsFalse(DocumentUriConversion.TryGetLocalPath("https://example.com/test.ext", out string filePath));
		Assert.AreEqual(string.Empty, filePath);
	}

	[TestMethod]
	public void NormalizeLocalPath_Uri_RejectsNonFileUri()
	{
		Assert.ThrowsExactly<ArgumentException>(() =>
			DocumentUriConversion.NormalizeLocalPath(new Uri("https://example.com/test.ext"), supportsUncAuthority: true)
		);

		Assert.ThrowsExactly<ArgumentException>(() =>
			DocumentUriConversion.NormalizeLocalPath(new Uri("relative/path.ext", UriKind.Relative), supportsUncAuthority: true)
		);
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	public void NormalizeLocalPath_Uri_FileUriWithDriveLetter_ReturnsLocalPath()
	{
		Uri uri = new("file:///C:/test/folder/test.ext");
		string expectedPath = Path.GetFullPath(@"C:\test\folder\test.ext");

		Assert.AreEqual(expectedPath, DocumentUriConversion.NormalizeLocalPath(uri, supportsUncAuthority: true));
	}

	[TestMethod]
	[OSCondition(OperatingSystems.Windows)]
	[DataRow("file:///c%3A/test/folder/test.ext")]
	[DataRow("file:///C%3a/test/folder/test.ext")]
	public void TryGetLocalPath_FileUriWithAnEscapedDriveColon_ReturnsTheLocalPath(string uri)
	{
		// The Lua language server escapes the colon of the drive letter, as VS Code does.
		Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(Path.GetFullPath(@"C:\test\folder\test.ext"), filePath, ignoreCase: true);
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_UsesAuthorityLessThreeSlashShape()
	{
		// A rooted POSIX path must not be concatenated verbatim: four slashes would parse as a UNC authority on
		// non-Windows hosts, which corrupts every document URI.
		Assert.AreEqual("file:///home/user/workspace/main.lua", DocumentUriConversion.BuildFileUri("/home/user/workspace/main.lua"));
	}

	[TestMethod]
	public void BuildFileUri_PosixAbsolutePath_IsNotParsedAsUncAuthority() =>
		Assert.AreEqual(string.Empty, new Uri(DocumentUriConversion.BuildFileUri("/home/user/x")).Host);

	[TestMethod]
	public void BuildFileUri_UncPath_BecomesTheUriAuthority() =>
		Assert.AreEqual("file://server/share/dir/file.lua", DocumentUriConversion.BuildFileUri(@"\\server\share\dir\file.lua"));

	[TestMethod]
	public void BuildFileUri_DrivePath_UsesThreeSlashes() =>
		Assert.AreEqual("file:///C:/temp/a.txt", DocumentUriConversion.BuildFileUri(@"C:\temp\a.txt"));

	[TestMethod]
	public void BuildFileUri_ExtendedLengthPrefixes_AreStrippedBeforeBuilding()
	{
		Assert.AreEqual("file:///C:/temp/a.txt", DocumentUriConversion.BuildFileUri(@"\\?\C:\temp\a.txt"));
		Assert.AreEqual("file://server/share/x.lua", DocumentUriConversion.BuildFileUri(@"\\?\UNC\server\share\x.lua"));
	}

	[TestMethod]
	public void BuildFileUri_EscapesPercentHashAndQuestionMarkCharacters() =>
		Assert.AreEqual("file:///home/user/a%23b%3Fc%25d.txt", DocumentUriConversion.BuildFileUri("/home/user/a#b?c%d.txt"));

	[TestMethod]
	public void CreateFileUri_WithHashAndQuestionMarkCharacters_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "a#b?c.txt"));

		string uri = DocumentUriConversion.CreateFileUri(expectedPath);

		Assert.IsTrue(uri.Contains("a%23b%3Fc.txt", StringComparison.Ordinal), uri);
		Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath));
		Assert.AreEqual(expectedPath, filePath);
	}

	[TestMethod]
	public void CreateFileUri_WithNonAsciiFileName_RoundTripsThroughTryGetLocalPath()
	{
		string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", "日本語 test.txt"));

		string uri = DocumentUriConversion.CreateFileUri(expectedPath);

		Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath));
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
			"a b.txt",
			"a!b.txt",
			"a$b.txt",
			"a&b.txt",
			"a'b.txt",
			"a(b).txt",
			"a*b.txt",
			"a+b.txt",
			"a,b.txt",
			"a;b.txt",
			"a=b.txt",
			"a@b.txt",
			"a~b.txt",
			"a-b_c.d.txt",
			"a[b].txt",
			"a{b}.txt",
			"a^b.txt",
			"a`b.txt",
			"a<b>.txt",
			"a\"b.txt",
			"a|b.txt",
			"a:b.txt",
		];

		foreach (string fileName in fileNames)
		{
			string expectedPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Path Helper", fileName));

			string uri = DocumentUriConversion.CreateFileUri(expectedPath);

			Assert.IsTrue(DocumentUriConversion.TryGetLocalPath(uri, out string filePath), $"'{fileName}' produced '{uri}'.");
			Assert.AreEqual(expectedPath, filePath, $"'{fileName}' did not round-trip through '{uri}'.");
		}
	}

	[TestMethod]
	[DataRow("relative/file.lua")]
	[DataRow("file.lua")]
	public void CreateFileUri_WithARelativePath_Throws(string relativePath) =>
		Assert.ThrowsExactly<ArgumentException>(() => DocumentUriConversion.CreateFileUri(relativePath));
}
