using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using Nickelony.IDEKit.IntelliSense.Tests.TestSupport;

namespace Nickelony.IDEKit.IntelliSense.Tests.Hover;

[TestClass]
public sealed class TextHoverTests
{
	[TestMethod]
	public void Request_StoresSnapshotAndOffset()
	{
		var request = new TextHoverRequest("local value", 6);

		Assert.AreEqual("local value", request.DocumentText);
		Assert.AreEqual(6, request.HoveredOffset);
	}

	[TestMethod]
	public void Request_OffsetAtTextLength_IsAccepted()
	{
		var request = new TextHoverRequest("abc", 3);

		Assert.AreEqual(3, request.HoveredOffset);
	}

	[TestMethod]
	public void Request_NegativeOffset_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextHoverRequest("abc", -1));
	}

	[TestMethod]
	public void Request_OffsetBeyondTextLength_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextHoverRequest("abc", 4));
	}

	[TestMethod]
	public void Info_DefaultsToPlainTextWithoutOptionalValues()
	{
		var info = new TextHoverInfo("content");

		Assert.AreEqual("content", info.Content);
		Assert.AreEqual(TextMarkupKind.PlainText, info.ContentKind);
		Assert.IsNull(info.SymbolName);
		Assert.IsNull(info.Range);
		Assert.IsNull(info.DefinitionDiscriminator);
	}

	[TestMethod]
	public void Info_StoresMarkdownContentRangeAndDiscriminator()
	{
		var discriminator = new TestDiscriminator("header");
		var range = new TextRange(3, 4);

		var info = new TextHoverInfo("**text**", TextMarkupKind.Markdown)
		{
			SymbolName = "value",
			Range = range,
			DefinitionDiscriminator = discriminator
		};

		Assert.AreEqual("**text**", info.Content);
		Assert.AreEqual(TextMarkupKind.Markdown, info.ContentKind);
		Assert.AreEqual("value", info.SymbolName);
		Assert.AreEqual(range, info.Range);
		Assert.AreSame(discriminator, info.DefinitionDiscriminator);
	}

	[TestMethod]
	public void Info_NullContent_Throws()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextHoverInfo(null!));
	}

	[TestMethod]
	public void Info_BlankContent_Throws()
	{
		Assert.ThrowsExactly<ArgumentException>(() => new TextHoverInfo("   "));
	}

	[TestMethod]
	public void Info_UndefinedContentKind_Throws()
	{
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextHoverInfo("content", (TextMarkupKind)99));
	}

	[TestMethod]
	public void Info_SymbolName_IsTrimmedAndBlankBecomesNull()
	{
		var info = new TextHoverInfo("content") { SymbolName = "  value  " };

		Assert.AreEqual("value", info.SymbolName);

		var blank = new TextHoverInfo("content") { SymbolName = "   " };

		Assert.IsNull(blank.SymbolName);
	}

	[TestMethod]
	public void Request_Equality_ComparesAllComponents()
	{
		var request = new TextHoverRequest("local value", 6);

		Assert.AreEqual(request, new TextHoverRequest("local value", 6));
		Assert.AreNotEqual(request, new TextHoverRequest("local value", 5));
		Assert.AreNotEqual(request, new TextHoverRequest("other value", 6));
	}

	[TestMethod]
	public void Info_Equality_ComparesEveryComponent()
	{
		static TextHoverInfo CreateInfo(
			string content = "content",
			TextMarkupKind contentKind = TextMarkupKind.Markdown,
			string? symbolName = "value",
			TextRange? range = null,
			TextDefinitionDiscriminator? discriminator = null)
			=> new(content, contentKind)
			{
				SymbolName = symbolName,
				Range = range,
				DefinitionDiscriminator = discriminator
			};

		var range = new TextRange(3, 4);
		TextHoverInfo baseline = CreateInfo(range: range, discriminator: new TestDiscriminator("header"));

		Assert.AreEqual(baseline, CreateInfo(range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreEqual(baseline.GetHashCode(), CreateInfo(range: range, discriminator: new TestDiscriminator("header")).GetHashCode());

		Assert.AreNotEqual(baseline, CreateInfo(content: "other", range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(contentKind: TextMarkupKind.PlainText, range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(symbolName: "other", range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(symbolName: null, range: range, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: new TextRange(4, 4), discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: null, discriminator: new TestDiscriminator("header")));
		Assert.AreNotEqual(baseline, CreateInfo(range: range, discriminator: new TestDiscriminator("body")));
		Assert.AreNotEqual(baseline, CreateInfo(range: range));
	}
}
