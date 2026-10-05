namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>Pins the copy-on-assign behavior and the defaults of <see cref="MarkdownRenderOptions"/>.</summary>
[TestClass]
public sealed class MarkdownRenderOptionsTests
{
	[TestMethod]
	public void DefaultOptions_HyperlinkSchemes_CannotBeMutatedThroughTheInterface()
	{
		var schemes = (ICollection<string>)MarkdownRenderOptions.Default.SupportedHyperlinkSchemes;
		Assert.ThrowsExactly<NotSupportedException>(() => schemes.Add("ftp"));
	}

	[TestMethod]
	public void DefaultOptions_HighlightingAliases_CannotBeMutatedThroughTheInterface()
	{
		var aliases = (IDictionary<string, string>)MarkdownRenderOptions.Default.HighlightingAliases;
		Assert.ThrowsExactly<NotSupportedException>(() => aliases.Add("probe", ".cs"));
	}

	[TestMethod]
	public void Options_AssignedHyperlinkSchemes_AreCopiedFrozenAndCaseInsensitive()
	{
		var schemes = new HashSet<string>(StringComparer.Ordinal) { "ftp" };
		var options = new MarkdownRenderOptions { SupportedHyperlinkSchemes = schemes };

		schemes.Add("file");

		// The options hold a frozen case-insensitive copy of the assigned set: the later source change
		// is not observed, the stored set cannot be mutated, and scheme casing does not matter.
		Assert.Contains("FTP", options.SupportedHyperlinkSchemes);
		Assert.DoesNotContain("file", options.SupportedHyperlinkSchemes);

		var storedSchemes = (ICollection<string>)options.SupportedHyperlinkSchemes;

		Assert.ThrowsExactly<NotSupportedException>(() => storedSchemes.Add("mailto"));
	}

	[TestMethod]
	public void Options_AssignedHighlightingAliases_AreCopiedFrozenAndCaseInsensitive()
	{
		var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { ["MyLang"] = ".cs" };
		var options = new MarkdownRenderOptions { HighlightingAliases = aliases };

		aliases["MyLang"] = ".json";
		aliases["other"] = ".txt";

		// The options hold a frozen case-insensitive copy of the assigned map: the later source changes
		// are not observed, the stored map cannot be mutated, and key casing does not matter.
		Assert.AreEqual(".cs", options.HighlightingAliases["MYLANG"]);
		Assert.IsFalse(options.HighlightingAliases.ContainsKey("other"));

		var storedAliases = (IDictionary<string, string>)options.HighlightingAliases;

		Assert.ThrowsExactly<NotSupportedException>(() => storedAliases.Add("probe", ".cs"));
	}

	[TestMethod]
	public void DefaultOptions_AllowWheelChaining_DefaultsToTrue() => Assert.IsTrue(MarkdownRenderOptions.Default.AllowWheelChaining);

	[TestMethod]
	public void DefaultOptions_HighlightingAliases_AreEmpty()
	{
		// The package ships no alias mappings: mapping a fence name the engine does not know to a
		// definition is the host content's convention, supplied through this option.
		IReadOnlyDictionary<string, string> aliases = MarkdownRenderOptions.Default.HighlightingAliases;
		Assert.IsEmpty(aliases);
	}
}
