using Nickelony.IDEKit.IntelliSense.CodeActions;

namespace Nickelony.IDEKit.IntelliSense.Tests.CodeActions;

/// <summary>
/// Verifies the code-action editor-state context: storage, offset-only equality, and construction guards.
/// </summary>
[TestClass]
public sealed class TextCodeActionContextTests
{
	[TestMethod]
	public void Constructor_StoresEditorState()
	{
		var context = new TextCodeActionContext("text", caretOffset: 2, selectionStartOffset: 1, selectionEndOffset: 3);

		Assert.AreEqual("text", context.DocumentText);
		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(1, context.SelectionStartOffset);
		Assert.AreEqual(3, context.SelectionEndOffset);
	}

	[TestMethod]
	public void Equality_ComparesOffsetsOnly()
	{
		var context = new TextCodeActionContext("text", 2, 1, 3);

		Assert.AreEqual(context, new TextCodeActionContext("text", 2, 1, 3));
		Assert.AreNotEqual(context, new TextCodeActionContext("text", 3, 1, 3));
		Assert.AreNotEqual(context, new TextCodeActionContext("text", 2, 0, 3));
		Assert.AreNotEqual(context, new TextCodeActionContext("text", 2, 1, 4));
	}

	[TestMethod]
	public void Equality_ExcludesDocumentText()
	{
		var context = new TextCodeActionContext("text", 2, 1, 3);

		// The document text is deliberately excluded: the same offsets over different text are equal,
		// so comparing contexts never materializes the lazily-carried text.
		Assert.AreEqual(context, new TextCodeActionContext("other", 2, 1, 3));
		Assert.AreEqual(context.GetHashCode(), new TextCodeActionContext("other", 2, 1, 3).GetHashCode());
	}

	[TestMethod]
	public void Constructor_InvalidOffsets_Throw()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextCodeActionContext(null!, 0, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 5, 0, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 0, 3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TextCodeActionContext("text", 0, -1, 0));
	}

	[TestMethod]
	public void FromDocument_LazilyMaterializedText_ValidatesOffsetsWithoutMaterializing()
	{
		int calls = 0;
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromFactory(4, () =>
		{
			calls++;
			return "text";
		});

		var context = TextCodeActionContext.FromDocument(document, caretOffset: 2, selectionStartOffset: 1, selectionEndOffset: 3);

		// The offsets are validated against the declared length, so creating the context never
		// materializes the text; the first document-text read does.
		Assert.AreEqual(0, calls);
		Assert.AreEqual("text", context.DocumentText);
		Assert.AreEqual(1, calls);
	}

	[TestMethod]
	public void FromDocument_OffsetBeyondDeclaredLength_ThrowsWithoutMaterializing()
	{
		int calls = 0;
		TextCodeActionDocumentText document = TextCodeActionDocumentText.FromFactory(4, () =>
		{
			calls++;
			return "text";
		});

		Assert.ThrowsExactly<ArgumentOutOfRangeException>(
			() => TextCodeActionContext.FromDocument(document, caretOffset: 5, selectionStartOffset: 0, selectionEndOffset: 0));
		Assert.AreEqual(0, calls);
	}
}
