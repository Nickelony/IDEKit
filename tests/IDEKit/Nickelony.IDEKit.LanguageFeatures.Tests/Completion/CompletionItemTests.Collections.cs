using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Tests;

public sealed partial class CompletionItemTests
{
	[TestMethod]
	public void Tags_NotInitialized_IsEmpty()
	{
		var item = new CompletionItem("label");
		Assert.IsEmpty(item.Tags);
	}

	[TestMethod]
	public void Tags_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { Tags = [CompletionItemTag.Deprecated] };
		Assert.AreSequenceEqual([CompletionItemTag.Deprecated], [.. item.Tags]);
	}

	[TestMethod]
	public void Tags_Null_IsEmpty()
	{
		var item = new CompletionItem("label") { Tags = null! };
		Assert.IsEmpty(item.Tags);
	}

	[TestMethod]
	public void Tags_AssignedList_IsStoredAsSnapshot()
	{
		var tags = new List<CompletionItemTag> { CompletionItemTag.Deprecated };
		var item = new CompletionItem("label") { Tags = tags };

		tags.Clear();

		Assert.HasCount(1, item.Tags);
	}

	[TestMethod]
	public void Tags_UndefinedValue_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			new CompletionItem("label") { Tags = [CompletionItemTag.Deprecated, (CompletionItemTag)42] }
		);

		Assert.Contains("index 1", exception.Message, "The error should identify the offending element.");
	}

	[TestMethod]
	public void CommitCharacters_NotInitialized_IsEmpty()
	{
		var item = new CompletionItem("label");
		Assert.IsEmpty(item.CommitCharacters);
	}

	[TestMethod]
	public void CommitCharacters_Explicit_IsPreserved()
	{
		var item = new CompletionItem("label") { CommitCharacters = ["(", ","] };
		Assert.AreSequenceEqual(["(", ","], [.. item.CommitCharacters]);
	}

	[TestMethod]
	public void CommitCharacters_Null_IsEmpty()
	{
		var item = new CompletionItem("label") { CommitCharacters = null! };
		Assert.IsEmpty(item.CommitCharacters);
	}

	[TestMethod]
	public void CommitCharacters_AssignedList_IsStoredAsSnapshot()
	{
		var characters = new List<string> { "(", "," };
		var item = new CompletionItem("label") { CommitCharacters = characters };

		characters.Clear();

		Assert.HasCount(2, item.CommitCharacters);
	}

	[TestMethod]
	public void CommitCharacters_NullEntry_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(() => new CompletionItem("label") { CommitCharacters = ["(", null!] });
		Assert.Contains("index 1", exception.Message, "The error should identify the offending element.");
	}

	[TestMethod]
	public void AdditionalTextEdits_NotInitialized_IsEmpty()
	{
		var item = new CompletionItem("label");
		Assert.IsEmpty(item.AdditionalTextEdits);
	}

	[TestMethod]
	public void AdditionalTextEdits_Explicit_IsPreserved()
	{
		var edit = new CompletionTextEdit(new TextRange(0, 0), newText: "local print = print\n");
		var item = new CompletionItem("label") { AdditionalTextEdits = [edit] };

		Assert.HasCount(1, item.AdditionalTextEdits);
		Assert.AreEqual("local print = print\n", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void AdditionalTextEdits_Null_IsEmpty()
	{
		var item = new CompletionItem("label") { AdditionalTextEdits = null! };
		Assert.IsEmpty(item.AdditionalTextEdits);
	}

	[TestMethod]
	public void AdditionalTextEdits_AssignedList_IsStoredAsSnapshot()
	{
		var edits = new List<CompletionTextEdit> { new(new TextRange(0, 0), newText: "import") };
		var item = new CompletionItem("label") { AdditionalTextEdits = edits };

		edits.Clear();

		Assert.HasCount(1, item.AdditionalTextEdits);
		Assert.AreEqual("import", item.AdditionalTextEdits[0].NewText);
	}
}
