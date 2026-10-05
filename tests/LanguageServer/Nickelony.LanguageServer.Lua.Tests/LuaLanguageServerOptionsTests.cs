namespace Nickelony.LanguageServer.Lua.Tests;

/// <summary>Pins the Lua options record: its lists take owned snapshots, and a value that cannot configure the server is rejected.</summary>
[TestClass]
public sealed class LuaLanguageServerOptionsTests
{
	[TestMethod]
	public void OptionLists_TakeOwnedSnapshots()
	{
		var disabledDiagnostics = new List<string> { "unused-local" };
		var additionalDirectories = new List<string> { Path.Combine(TestPaths.Root, "Libraries", "Extra") };

		var options = new LuaLanguageServerOptions
		{
			DisabledDiagnostics = disabledDiagnostics,
			AdditionalLibraryDirectories = additionalDirectories,
		};

		disabledDiagnostics.Add("another");
		additionalDirectories.Clear();

		Assert.HasCount(1, options.DisabledDiagnostics);
		Assert.HasCount(1, options.AdditionalLibraryDirectories);

		// The snapshots are read-only: a downcast mutation must not be able to poison the shared
		// default options instance (a collection expression targeting IReadOnlyList rejects mutation
		// through IList with NotSupportedException).
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)options.DisabledDiagnostics)[0] = "changed");
		Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)options.AdditionalLibraryDirectories)[0] = "changed");
	}

	[TestMethod]
	public void RuntimeVersion_WithBlankValue_IsRejected()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LuaLanguageServerOptions { RuntimeVersion = null! });
		Assert.ThrowsExactly<ArgumentException>(() => new LuaLanguageServerOptions { RuntimeVersion = "   " });
	}

	[TestMethod]
	public void ListProperties_WithNullValue_AreRejected()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new LuaLanguageServerOptions { AdditionalLibraryDirectories = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new LuaLanguageServerOptions { DisabledDiagnostics = null! });
	}
}
