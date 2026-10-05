using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the code-action editor-state context: storage, offset-only equality, and construction guards.</summary>
[TestClass]
public sealed class CodeActionSelectionTests
{
	private static CodeActionSelection Create(string text = "text", int caretOffset = 2, TextRange? selection = null) =>
		new(new StringTextSnapshot(text), caretOffset, selection ?? new TextRange(1, 2));

	[TestMethod]
	public void Constructor_StoresEditorState()
	{
		CodeActionSelection context = Create();

		Assert.AreEqual("text", context.GetText());
		Assert.AreEqual(4, context.Snapshot.TextLength);
		Assert.AreEqual(2, context.CaretOffset);
		Assert.AreEqual(new TextRange(1, 2), context.Range);
	}

	[TestMethod]
	public void Equality_ComparesOffsetsOnly()
	{
		CodeActionSelection context = Create();

		Assert.AreEqual(context, Create());
		Assert.AreNotEqual(context, Create(caretOffset: 3));
		Assert.AreNotEqual(context, Create(selection: new TextRange(0, 3)));
		Assert.AreNotEqual(context, Create(selection: new TextRange(1, 3)));
	}

	[TestMethod]
	public void Equality_ExcludesTheSnapshot()
	{
		CodeActionSelection context = Create();

		Assert.AreEqual(context, Create(text: "other"));
		Assert.AreEqual(context.GetHashCode(), Create(text: "other").GetHashCode());
	}

	[TestMethod]
	public void Constructor_NullSnapshot_Throws() =>
		Assert.ThrowsExactly<ArgumentNullException>(() => new CodeActionSelection(null!, caretOffset: 0, range: default));

	[TestMethod]
	public void Constructor_OffsetsOutsideTheSnapshot_ThrowTheNamedParameter()
	{
		ArgumentOutOfRangeException caretException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			Create(caretOffset: 5, selection: new TextRange(0, 0))
		);

		Assert.AreEqual("caretOffset", caretException.ParamName);

		ArgumentOutOfRangeException selectionException = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
			Create(caretOffset: 0, selection: new TextRange(3, 2))
		);

		Assert.AreEqual("range", selectionException.ParamName);
	}

	[TestMethod]
	public void ToString_DoesNotReadTheText()
	{
		CodeActionSelection context = Create();
		Assert.DoesNotContain("text", context.ToString());
	}
}
