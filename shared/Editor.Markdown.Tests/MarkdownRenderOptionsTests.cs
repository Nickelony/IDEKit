#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.Markdown.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.Markdown.Tests;
#endif

/// <summary>
/// Pins the toolkit-neutral copy-on-assign behavior of <see cref="MarkdownRenderOptions"/>.
/// </summary>
[TestClass]
public sealed class MarkdownRenderOptionsTests
{
	[TestMethod]
	public void DefaultOptions_HyperlinkSchemes_CannotBeMutatedThroughTheInterface()
	{
		ICollection<string> schemes = (ICollection<string>)MarkdownRenderOptions.Default.SupportedHyperlinkSchemes;

		Assert.ThrowsExactly<NotSupportedException>(() => schemes.Add("ftp"));
	}

	[TestMethod]
	public void DefaultOptions_HighlightingAliases_CannotBeMutatedThroughTheInterface()
	{
		IDictionary<string, string> aliases = (IDictionary<string, string>)MarkdownRenderOptions.Default.HighlightingAliases;

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
		Assert.IsTrue(options.SupportedHyperlinkSchemes.Contains("FTP"));
		Assert.IsFalse(options.SupportedHyperlinkSchemes.Contains("file"));

		ICollection<string> storedSchemes = (ICollection<string>)options.SupportedHyperlinkSchemes;

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

		IDictionary<string, string> storedAliases = (IDictionary<string, string>)options.HighlightingAliases;

		Assert.ThrowsExactly<NotSupportedException>(() => storedAliases.Add("probe", ".cs"));
	}

	[TestMethod]
	public void DefaultOptions_AllowWheelChaining_DefaultsToTrue()
	{
		Assert.IsTrue(MarkdownRenderOptions.Default.AllowWheelChaining);
	}

	[TestMethod]
	public void CreateCodeBlockEditor_DefaultAliases_AreEmpty()
	{
		// The package ships no alias mappings: mapping a fence name the engine does not know to a
		// definition is the host content's convention, supplied through this option.
		IReadOnlyDictionary<string, string> aliases = MarkdownRenderOptions.Default.HighlightingAliases;

		Assert.AreEqual(0, aliases.Count);
	}
}
