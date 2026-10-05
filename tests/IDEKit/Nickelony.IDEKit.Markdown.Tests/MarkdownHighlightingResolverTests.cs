using System.Collections.Frozen;

namespace Nickelony.IDEKit.Markdown.Tests;

/// <summary>
/// Pins the resolution order of <see cref="MarkdownHighlightingResolver"/> with a fake registry, so the rules the editor bindings share
/// are covered once.
/// </summary>
[TestClass]
public sealed class MarkdownHighlightingResolverTests
{
	private static readonly FrozenDictionary<string, string> s_noAliases = FrozenDictionary.ToFrozenDictionary<string, string>(
		[],
		StringComparer.OrdinalIgnoreCase
	);

	private static FrozenDictionary<string, string> Aliases(params (string Key, string Value)[] entries)
	{
		return FrozenDictionary.ToFrozenDictionary(
			entries.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value)),
			StringComparer.OrdinalIgnoreCase
		);
	}

	[TestMethod]
	public void Resolve_DefinitionNameWithSuppliedCasing_IsFoundFirst()
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], []);
		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve("CSharp", s_noAliases, registry));
	}

	[TestMethod]
	public void Resolve_DefinitionNameWithDifferentCasing_IsFoundByTheInsensitiveScan()
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], []);
		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve("csharp", s_noAliases, registry));
	}

	[TestMethod]
	public void Resolve_FileExtension_IsFoundWithOrWithoutTheLeadingDot()
	{
		var registry = new FakeHighlightingCatalog([], [(".cs", "CSharp")]);

		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve("cs", s_noAliases, registry));
		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve(".cs", s_noAliases, registry));
	}

	[TestMethod]
	public void Resolve_Alias_IsConsultedOnlyAfterNameAndExtensionFail()
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], [(".cs", "CSharp")]);
		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve("mylang", Aliases(("mylang", "cs")), registry));
	}

	[TestMethod]
	public void Resolve_AliasTarget_CannotOverrideAnExactNameMatch()
	{
		var registry = new FakeHighlightingCatalog(["mylang"], [(".cs", "CSharp")]);

		// The token already matches a definition name, so the alias is never consulted.
		Assert.AreEqual("mylang", MarkdownHighlightingResolver.Resolve("mylang", Aliases(("mylang", "cs")), registry));
	}

	[TestMethod]
	public void Resolve_UnknownLanguage_ReturnsNull()
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], [(".cs", "CSharp")]);
		Assert.IsNull(MarkdownHighlightingResolver.Resolve("definitely-not-a-language", s_noAliases, registry));
	}

	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[TestMethod]
	public void Resolve_BlankLanguage_ReturnsNull(string? language)
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], [(".cs", "CSharp")]);
		Assert.IsNull(MarkdownHighlightingResolver.Resolve(language, s_noAliases, registry));
	}

	[TestMethod]
	public void Resolve_LanguageIsTrimmedBeforeResolution()
	{
		var registry = new FakeHighlightingCatalog(["CSharp"], []);
		Assert.AreEqual("CSharp", MarkdownHighlightingResolver.Resolve("  CSharp  ", s_noAliases, registry));
	}

	private sealed class FakeHighlightingCatalog : IMarkdownHighlightingCatalog<string>
	{
		private readonly IReadOnlyList<string> _definitions;
		private readonly IReadOnlyDictionary<string, string> _extensions;

		public FakeHighlightingCatalog(IReadOnlyList<string> definitions, IReadOnlyList<(string Extension, string Definition)> extensions)
		{
			_definitions = definitions;
			_extensions = extensions.ToDictionary(entry => entry.Extension, entry => entry.Definition, StringComparer.OrdinalIgnoreCase);
		}

		public string? GetDefinition(string name) => _definitions.Contains(name, StringComparer.Ordinal) ? name : null;

		public IReadOnlyList<string> GetDefinitions() => _definitions;

		public string? GetDefinitionByExtension(string extension) => _extensions.TryGetValue(extension, out string? definition) ? definition : null;

		public string GetName(string definition) => definition;
	}
}
