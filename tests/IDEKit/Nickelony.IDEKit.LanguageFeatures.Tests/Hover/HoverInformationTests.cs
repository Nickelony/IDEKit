using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Testing;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

[TestClass]
public sealed class HoverInformationTests
{
	[TestMethod]
	public void Info_DefaultsToPlainTextWithoutOptionalValues()
	{
		var info = new HoverInformation("content");

		Assert.AreEqual("content", info.Content.Value);
		Assert.AreEqual(MarkupKind.PlainText, info.Content.Kind);
		Assert.IsNull(info.SymbolName);
		Assert.IsNull(info.Range);
		Assert.IsNull(info.DefinitionDiscriminator);
	}

	[TestMethod]
	public void Info_StoresMarkdownContentRangeAndDiscriminator()
	{
		var discriminator = new TestDiscriminator("header");
		var range = new TextRange(3, 4);

		var info = new HoverInformation(new MarkupContent("**text**", MarkupKind.Markdown))
		{
			SymbolName = "value",
			Range = range,
			DefinitionDiscriminator = discriminator,
		};

		Assert.AreEqual("**text**", info.Content.Value);
		Assert.AreEqual(MarkupKind.Markdown, info.Content.Kind);
		Assert.AreEqual("value", info.SymbolName);
		Assert.AreEqual(range, info.Range);
		Assert.AreSame(discriminator, info.DefinitionDiscriminator);
	}

	[TestMethod]
	public void Info_NullContent_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new HoverInformation(null!));

	[TestMethod]
	public void Info_BlankContent_Throws() => Assert.ThrowsExactly<ArgumentException>(() => new HoverInformation("   "));

	[TestMethod]
	public void Info_SymbolName_IsTrimmedAndBlankBecomesNull()
	{
		var info = new HoverInformation("content") { SymbolName = "  value  " };

		Assert.AreEqual("value", info.SymbolName);

		var blank = new HoverInformation("content") { SymbolName = "   " };

		Assert.IsNull(blank.SymbolName);
	}

	[TestMethod]
	public void Info_Equality_ComparesEveryComponent()
	{
		static HoverInformation CreateInfo(
			string content = "content",
			MarkupKind contentKind = MarkupKind.Markdown,
			string? symbolName = "value",
			TextRange? range = null,
			DefinitionDiscriminator? discriminator = null
		)
		{
			return new(new MarkupContent(content, contentKind))
			{
				SymbolName = symbolName,
				Range = range,
				DefinitionDiscriminator = discriminator,
			};
		}

		var range = new TextRange(3, 4);

		HoverInformation baseline = CreateInfo(range: range, discriminator: new TestDiscriminator("header"));

		Assert.AreEqual(baseline, CreateInfo(range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreEqual(baseline.GetHashCode(), CreateInfo(range: range, discriminator: new TestDiscriminator("header")).GetHashCode());

		Assert.AreNotEqual(baseline, CreateInfo(content: "other", range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(contentKind: MarkupKind.PlainText, range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(symbolName: "other", range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(symbolName: null, range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: new TextRange(4, 4), discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: null, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: range, discriminator: new TestDiscriminator("body")));
		Assert.AreNotEqual(baseline, CreateInfo(range: range));
	}
}
