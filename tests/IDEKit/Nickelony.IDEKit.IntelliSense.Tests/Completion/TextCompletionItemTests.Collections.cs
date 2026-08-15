using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Completion;

namespace Nickelony.IDEKit.IntelliSense.Tests.Completion;

public sealed partial class TextCompletionItemTests
{
	[TestMethod]
	public void Tags_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.Tags.Count);
	}

	[TestMethod]
	public void Tags_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated] };
		CollectionAssert.AreEqual(new[] { TextCompletionTag.Deprecated }, item.Tags.ToArray());
	}

	[TestMethod]
	public void Tags_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { Tags = null! };
		Assert.AreEqual(0, item.Tags.Count);
	}

	[TestMethod]
	public void Tags_AssignedList_IsStoredAsSnapshot()
	{
		var tags = new List<TextCompletionTag> { TextCompletionTag.Deprecated };
		var item = new TextCompletionItem("label") { Tags = tags };

		tags.Clear();

		Assert.AreEqual(1, item.Tags.Count);
	}

	[TestMethod]
	public void Tags_UndefinedValue_Throws()
	{
		ArgumentOutOfRangeException exception = Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => new TextCompletionItem("label") { Tags = [TextCompletionTag.Deprecated, (TextCompletionTag)42] });

		StringAssert.Contains(exception.Message, "index 1", "The error should identify the offending element.");
	}

	[TestMethod]
	public void CommitCharacters_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void CommitCharacters_Explicit_IsPreserved()
	{
		var item = new TextCompletionItem("label") { CommitCharacters = ["(", ","] };
		CollectionAssert.AreEqual(new[] { "(", "," }, item.CommitCharacters.ToArray());
	}

	[TestMethod]
	public void CommitCharacters_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { CommitCharacters = null! };
		Assert.AreEqual(0, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void CommitCharacters_AssignedList_IsStoredAsSnapshot()
	{
		var characters = new List<string> { "(", "," };
		var item = new TextCompletionItem("label") { CommitCharacters = characters };

		characters.Clear();

		Assert.AreEqual(2, item.CommitCharacters.Count);
	}

	[TestMethod]
	public void CommitCharacters_NullEntry_Throws()
	{
		ArgumentException exception = Assert.ThrowsExactly<ArgumentException>(
			() => new TextCompletionItem("label") { CommitCharacters = ["(", null!] });

		StringAssert.Contains(exception.Message, "index 1", "The error should identify the offending element.");
	}

	[TestMethod]
	public void AdditionalTextEdits_NotInitialized_IsEmpty()
	{
		var item = new TextCompletionItem("label");
		Assert.AreEqual(0, item.AdditionalTextEdits.Count);
	}

	[TestMethod]
	public void AdditionalTextEdits_Explicit_IsPreserved()
	{
		var edit = new TextCompletionTextEdit(new TextRange(0, 0), newText: "local print = print\n");
		var item = new TextCompletionItem("label") { AdditionalTextEdits = [edit] };

		Assert.AreEqual(1, item.AdditionalTextEdits.Count);
		Assert.AreEqual("local print = print\n", item.AdditionalTextEdits[0].NewText);
	}

	[TestMethod]
	public void AdditionalTextEdits_Null_IsEmpty()
	{
		var item = new TextCompletionItem("label") { AdditionalTextEdits = null! };
		Assert.AreEqual(0, item.AdditionalTextEdits.Count);
	}

	[TestMethod]
	public void AdditionalTextEdits_AssignedList_IsStoredAsSnapshot()
	{
		var edits = new List<TextCompletionTextEdit> { new(new TextRange(0, 0), newText: "import") };
		var item = new TextCompletionItem("label") { AdditionalTextEdits = edits };

		edits.Clear();

		Assert.AreEqual(1, item.AdditionalTextEdits.Count);
		Assert.AreEqual("import", item.AdditionalTextEdits[0].NewText);
	}
}
