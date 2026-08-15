#if AVALONIAEDIT
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Documents;
using Nickelony.IDEKit.Core.Text;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Documents;
using Nickelony.IDEKit.Core.Text;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class TextDocumentSnapshotTests
{
	[TestMethod]
	public void Snapshot_DoesNotChangeWhenDocumentTextIsReplaced()
	{
		var document = new TextDocument("hello");
		var snapshot = new TextDocumentSnapshot(document);

		document.Text = "world!";

		Assert.AreEqual("hello", snapshot.GetText(0, snapshot.TextLength));
		Assert.AreEqual(5, snapshot.TextLength);
		Assert.AreEqual(1, snapshot.LineCount);
	}

	[TestMethod]
	public void Snapshot_DoesNotChangeWhenDocumentIsEdited()
	{
		var document = new TextDocument("line1\nline2");
		var snapshot = new TextDocumentSnapshot(document);

		document.Insert(6, " inserted");

		Assert.AreEqual("line1\nline2", snapshot.GetText(0, snapshot.TextLength));
		Assert.AreEqual(11, snapshot.TextLength);
		Assert.AreEqual(2, snapshot.LineCount);
	}

	[TestMethod]
	public void Snapshot_Lines_RemainStableAfterDocumentEdit()
	{
		var document = new TextDocument("first\nsecond");
		var snapshot = new TextDocumentSnapshot(document);

		ITextLine line = snapshot.GetLineByNumber(2);
		document.Insert(0, "X");

		Assert.AreEqual(6, line.Offset);
		Assert.AreEqual(6, line.Length);
		Assert.AreEqual(2, line.LineNumber);
		Assert.AreEqual("second", snapshot.GetText(line.Offset, line.Length));
	}

	[TestMethod]
	public void Snapshot_CapturesFileNameAtConstruction()
	{
		var document = new TextDocument("text")
		{
			FileName = @"C:\path\original.txt"
		};

		var snapshot = new TextDocumentSnapshot(document);

		document.FileName = @"C:\path\changed.txt";

		Assert.AreEqual(@"C:\path\original.txt", snapshot.FileName);
	}

	[TestMethod]
	public void Snapshot_DocumentWithoutFileName_ReturnsNullFileName()
	{
		var document = new TextDocument("text");

		var snapshot = new TextDocumentSnapshot(document);

		Assert.IsNull(snapshot.FileName);
	}

	[TestMethod]
	public void Snapshot_UsesUtf16CodeUnitOffsets()
	{
		// The emoji is a surrogate pair, so the text is "a" + two UTF-16 code units + "b".
		var snapshot = new TextDocumentSnapshot(new TextDocument("a\U0001F600b"));

		Assert.AreEqual(4, snapshot.TextLength);
		Assert.AreEqual(1, snapshot.LineCount);
		Assert.IsTrue(char.IsHighSurrogate(snapshot.GetCharAt(1)));
		Assert.IsTrue(char.IsLowSurrogate(snapshot.GetCharAt(2)));
		Assert.AreEqual("\U0001F600", snapshot.GetText(1, 2));
		Assert.AreEqual("b", snapshot.GetText(3, 1));
	}

	[TestMethod]
	public void Snapshot_CapturedOnOwnerThread_IsReadableFromAnotherThread()
	{
		var document = new TextDocument("hello");

		// The documented contract: capture the snapshot on the document's owner thread...
		var snapshot = new TextDocumentSnapshot(document);

		// ...then read it from another thread while the owner thread keeps the document.
		string? capturedText = null;
		string? capturedLineText = null;
		Exception? thrown = null;
		var thread = new Thread(() =>
		{
			try
			{
				ITextLine line = snapshot.GetLineByNumber(1);

				capturedText = snapshot.GetText(0, snapshot.TextLength);
				capturedLineText = snapshot.GetText(line.Offset, line.Length);
			}
			catch (Exception exception)
			{
				thrown = exception;
			}
		});
#if AVALONIAEDIT

		// The WPF reference suite ran the reader on an STA thread to mirror a UI thread. Avalonia has no
		// apartment model and SetApartmentState is unsupported on non-Windows platforms, so the reader is
		// a plain background thread: the snapshot's thread-agnostic read is what this test pins.
#else
		thread.SetApartmentState(ApartmentState.STA);
#endif
		thread.IsBackground = true;
		thread.Start();
		thread.Join(TimeSpan.FromSeconds(10));

		// A bounded join keeps a regression failing the test instead of hanging the test run.
		Assert.IsFalse(thread.IsAlive, "The snapshot read attempt did not complete within the timeout.");
		Assert.IsNull(thrown, $"Expected the snapshot to be readable from another thread, but it threw: {thrown}");
		Assert.AreEqual("hello", capturedText);
		Assert.AreEqual("hello", capturedLineText);
	}

	[TestMethod]
	public void Snapshot_ExposesTextLinesAndCharacters()
	{
		var snapshot = new TextDocumentSnapshot(new TextDocument("one\r\ntwo"));

		Assert.AreEqual(8, snapshot.TextLength);
		Assert.AreEqual(2, snapshot.LineCount);
		Assert.AreEqual('o', snapshot.GetCharAt(0));
		Assert.AreEqual("one", snapshot.GetText(0, 3));
		Assert.AreEqual(2, snapshot.GetLineByOffset(5).LineNumber);
		Assert.AreEqual(2, snapshot.GetLineByNumber(2).LineNumber);
		Assert.HasCount(2, snapshot.Lines);
	}

	[TestMethod]
	public void Snapshot_BoundaryCallsAtTextLength_FollowDocumentSemantics()
	{
		var snapshot = new TextDocumentSnapshot(new TextDocument("one\r\ntwo"));

		// An offset equal to the text length is a valid line and empty-range boundary: it maps to the
		// trailing position of the last line and reads as empty text, while a character read there is
		// out of range.
		Assert.AreEqual(2, snapshot.GetLineByOffset(snapshot.TextLength).LineNumber);
		Assert.AreEqual(string.Empty, snapshot.GetText(snapshot.TextLength, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(snapshot.TextLength));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(snapshot.TextLength, 1));
	}

	[TestMethod]
	public void Snapshot_InvalidArgumentRanges_ThrowArgumentOutOfRangeException()
	{
		var snapshot = new TextDocumentSnapshot(new TextDocument("text"));

		// The Core ITextSnapshot contract requires the argument exception for every invalid range.
		// The underlying the editor snapshot throws an overflow for a negative length, so the
		// wrapper validates the arguments itself.
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(-1, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(0, -1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(5, 0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(0, 5));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetText(3, 2));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetCharAt(4));
	}

	[TestMethod]
	public void Snapshot_OutOfRangeLineAccess_ThrowsArgumentOutOfRangeException()
	{
		var snapshot = new TextDocumentSnapshot(new TextDocument("one\r\ntwo"));

		// GetLineByNumber is one-based, and GetLineByOffset accepts only [0, TextLength].
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(0));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByNumber(snapshot.LineCount + 1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByOffset(-1));
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => snapshot.GetLineByOffset(snapshot.TextLength + 1));
	}
}
