#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class TextDocumentExtensionsTests
{
	[TestMethod]
	public void ClampOffset_WithinBounds_ReturnsOffset()
	{
		var document = new TextDocument("abcdef");
		Assert.AreEqual(3, document.ClampOffset(3));
	}

	[TestMethod]
	public void ClampOffset_NegativeOffset_ReturnsZero()
	{
		var document = new TextDocument("abcdef");
		Assert.AreEqual(0, document.ClampOffset(-1));
	}

	[TestMethod]
	public void ClampOffset_BeyondEnd_ReturnsTextLength()
	{
		var document = new TextDocument("abcdef");

		Assert.AreEqual(6, document.ClampOffset(6));
		Assert.AreEqual(6, document.ClampOffset(100));
	}

	[TestMethod]
	public void ClampOffset_EmptyDocument_ReturnsZero()
	{
		var document = new TextDocument();
		Assert.AreEqual(0, document.ClampOffset(10));
	}

	[TestMethod]
	public void GetLiveLine_SameDocument_ReturnsTheSameInstance()
	{
		var document = new TextDocument("one\r\ntwo");

		DocumentLine line = document.GetLineByNumber(2);

		Assert.AreSame(line, document.GetLiveLine(line));
	}

	[TestMethod]
	public void GetLiveLine_DeletedLine_ThrowsArgumentException()
	{
		var document = new TextDocument("one\r\ntwo");

		DocumentLine line = document.GetLineByNumber(2);

		// Replacing the whole content merges the old lines away, so the captured handle is deleted.
		document.Replace(0, document.TextLength, "single");

		Assert.IsTrue(line.IsDeleted, "The premise: the captured line handle is deleted.");
		Assert.ThrowsExactly<ArgumentException>(() => document.GetLiveLine(line));
	}

	[TestMethod]
	public void GetLiveLine_LineFromAnotherDocument_ThrowsArgumentException()
	{
		var document = new TextDocument("one\r\ntwo");
		var otherDocument = new TextDocument("one\r\ntwo");

		DocumentLine foreignLine = otherDocument.GetLineByNumber(1);

		// The handle describes the other document's line model even though the text is identical,
		// so its offsets and tree links must not be accepted against this document.
		Assert.ThrowsExactly<ArgumentException>(() => document.GetLiveLine(foreignLine));
	}

	[TestMethod]
	public void GetLiveLine_NullDocument_ThrowsArgumentNullException()
	{
		var document = new TextDocument("one");
		TextDocument? nullDocument = null;

		Assert.ThrowsExactly<ArgumentNullException>(() => nullDocument!.GetLiveLine(document.GetLineByNumber(1)));
	}

	[TestMethod]
	public void GetLiveLine_NullLine_ThrowsArgumentNullException()
	{
		var document = new TextDocument("one");

		Assert.ThrowsExactly<ArgumentNullException>(() => document.GetLiveLine(null!));
	}
}
