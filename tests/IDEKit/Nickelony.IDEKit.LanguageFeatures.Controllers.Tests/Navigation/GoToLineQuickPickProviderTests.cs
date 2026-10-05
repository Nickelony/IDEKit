using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Covers the go to line mode through a controller: the position it names, the preview that moves the caret while the user types,
/// the restore on Escape, and the clamp of a line and a column to the document.
/// </summary>
[TestClass]
public sealed class GoToLineQuickPickProviderTests
{
	private static FakeEditorNavigationTarget DocumentOf(int lineCount = 50, int lineLength = 7) =>
		new([.. Enumerable.Repeat(lineLength, lineCount)]);

	[TestMethod]
	public void Constructor_NullTargetGetter_Throws() => Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickProvider(null!));

	[TestMethod]
	public void Texts_DefaultToEnglishAndFollowTheOptions()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineCount: 9);
		IQuickPickProvider english = new GoToLineQuickPickProvider(() => target);

		Assert.AreEqual("Go to line", english.Name);
		Assert.AreEqual("Type a line number, or line:column, to go to", english.Placeholder);

		var options = new GoToLineQuickPickOptions
		{
			Name = "Zeile",
			Placeholder = "Zeile eingeben",
			NoEditorText = "Keine Datei",
			HintText = "Zeile {0}, Spalte {1} von {2}",
			LineText = "Zu Zeile {0}",
			LineColumnText = "Zu Zeile {0}, Spalte {1}",
		};

		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target, options));

		host.Controller.Open(":");

		Assert.AreEqual("Zeile", host.Providers[0].Name);
		Assert.AreEqual("Zeile eingeben", host.Controller.State.Placeholder);
		Assert.AreEqual("Zeile 1, Spalte 1 von 9", host.Controller.State.Rows.Single().Item!.Label);

		host.Controller.Open(":3");

		Assert.AreEqual("Zu Zeile 3", host.Controller.State.ActiveItem!.Label);

		host.Controller.Open(":3:2");

		Assert.AreEqual("Zu Zeile 3, Spalte 2", host.Controller.State.ActiveItem!.Label);
	}

	[TestMethod]
	public void Texts_AWindowWithoutAnEditorUsesTheNoEditorTextOfTheOptions()
	{
		using var host = new QuickPickTestHost(
			new GoToLineQuickPickProvider(() => null, new GoToLineQuickPickOptions { NoEditorText = "Keine Datei" })
		);

		host.Controller.Open(":");

		Assert.AreEqual("Keine Datei", host.Controller.State.Rows.Single().Item!.Label);
	}

	[TestMethod]
	public void Options_RejectANullText()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { Name = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { Placeholder = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { NoEditorText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { HintText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { LineText = null! });
		Assert.ThrowsExactly<ArgumentNullException>(() => new GoToLineQuickPickOptions { LineColumnText = null! });
	}

	[TestMethod]
	public void Open_APosition_ListsOneItemAndMovesTheCaretToIt()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12");

		Assert.AreSequenceEqual(["12:1"], host.Keys);
		Assert.AreEqual("Go to line 12", host.Controller.State.ActiveItem!.Label);
		Assert.AreSequenceEqual(["save", "move:12:1"], target.Log);
	}

	[TestMethod]
	public void Open_ALineAndAColumn_NamesBothAndMovesThere()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12:5");

		Assert.AreEqual("Go to line 12, column 5", host.Controller.State.ActiveItem!.Label);
		Assert.AreSequenceEqual(["save", "move:12:5"], target.Log);
	}

	[TestMethod]
	public void SetText_WhileTyping_MovesTheCaretWithEachPositionAndSavesTheViewOnce()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":");
		host.Controller.SetText(":1");
		host.PassDebounce();
		host.Controller.SetText(":15");
		host.PassDebounce();
		host.Controller.SetText(":15:3");
		host.PassDebounce();

		Assert.AreSequenceEqual(["save", "move:1:1", "move:15:1", "move:15:3"], target.Log);
	}

	[TestMethod]
	public void Dismiss_AfterAPreview_PutsTheViewBack()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":30:4");

		host.Controller.Dismiss();
		host.Controller.Dismiss();

		Assert.AreSequenceEqual(["save", "move:30:4", "restore"], target.Log);
		Assert.AreEqual(1, target.CaretLineNumber);
		Assert.AreEqual(1, target.CaretColumn);
	}

	[TestMethod]
	public void Dismiss_WithoutAPosition_RestoresNothing()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":");
		host.Controller.Dismiss();

		Assert.IsEmpty(target.Log);
	}

	[TestMethod]
	public void SetText_ToTextThatIsNoPosition_PutsTheViewBackAndMovesNothing()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12");
		host.Controller.SetText(":12x");
		host.PassDebounce();

		Assert.AreSequenceEqual(["save", "move:12:1", "restore"], target.Log);
		Assert.IsNull(host.Controller.State.ActiveItem);
		Assert.AreEqual(1, target.CaretLineNumber);
	}

	[TestMethod]
	public void SetText_ToAPositionAfterTextThatWasNone_SavesTheViewAgain()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12");
		host.Controller.SetText(":12x");
		host.PassDebounce();
		host.Controller.SetText(":20");
		host.PassDebounce();
		host.Controller.Dismiss();

		Assert.AreSequenceEqual(["save", "move:12:1", "restore", "save", "move:20:1", "restore"], target.Log);
	}

	[TestMethod]
	public void Open_TextThatIsNoPosition_ListsADimmedHintWithTheCurrentPositionAndTheRange()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineCount: 300);
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":");

		QuickPickItem hint = host.Controller.State.Rows.Single().Item!;

		Assert.IsFalse(hint.IsEnabled);
		Assert.Contains("Current line: 1, column: 1", hint.Label);
		Assert.Contains("between 1 and 300", hint.Label);
		Assert.IsNull(host.Controller.State.ActiveItem);
		Assert.IsFalse(host.Accept());
	}

	[TestMethod]
	public void SetText_ToTextThatIsNoPositionDuringAPreview_TellsWhereTheCaretStoodBeforeThePreview()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineCount: 100);

		target.MoveCaret(10, 3);
		target.Log.Clear();

		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":50");
		host.Controller.SetText(":50x");
		host.PassDebounce();

		QuickPickItem hint = host.Controller.State.Rows.Single().Item!;

		Assert.Contains("Current line: 10, column: 3", hint.Label);
		Assert.AreEqual(10, target.CaretLineNumber);
		Assert.AreSequenceEqual(["save", "move:50:1", "restore"], target.Log);
	}

	[TestMethod]
	public void SetText_ToTextThatIsNoPositionAfterThePreviewEnded_TellsTheLiveCaret()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineCount: 100);
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":");
		target.MoveCaret(40, 2);
		host.Controller.SetText(":x");
		host.PassDebounce();

		Assert.Contains("Current line: 40, column: 2", host.Controller.State.Rows.Single().Item!.Label);
	}

	[TestMethod]
	public void Open_NoEditor_ListsAHintAndAcceptsNothing()
	{
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => null));

		host.Controller.Open(":12");

		Assert.IsFalse(host.Controller.State.Rows.Single().Item!.IsEnabled);
		Assert.IsFalse(host.Accept());
		Assert.IsEmpty(host.Faults);
	}

	[TestMethod]
	public void Open_ALineBeyondTheDocument_ClampsToTheLastLine()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineCount: 50);
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":999");

		Assert.AreEqual("Go to line 50", host.Controller.State.ActiveItem!.Label);
		Assert.AreSequenceEqual(["save", "move:50:1"], target.Log);
	}

	[TestMethod]
	public void Open_AColumnBeyondTheLine_ClampsToTheColumnAfterTheLastCharacter()
	{
		FakeEditorNavigationTarget target = DocumentOf(lineLength: 7);
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":2:999");

		Assert.AreEqual("Go to line 2, column 8", host.Controller.State.ActiveItem!.Label);
		Assert.AreSequenceEqual(["save", "move:2:8"], target.Log);
	}

	[TestMethod]
	public void AcceptAsync_KeepsThePositionAndNeverPutsTheViewBack()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12:3");

		Assert.IsTrue(host.Accept());

		Assert.DoesNotContain("restore", target.Log);
		Assert.AreEqual(12, target.CaretLineNumber);
		Assert.AreEqual(3, target.CaretColumn);
		Assert.IsFalse(host.Controller.State.IsOpen);
	}

	[TestMethod]
	public void AcceptAsync_Twice_ForgetsTheFirstViewSoTheNextPreviewSavesThePositionOfTheFirstAccept()
	{
		FakeEditorNavigationTarget target = DocumentOf();
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target));

		host.Controller.Open(":12");
		Assert.IsTrue(host.Accept());

		target.Log.Clear();
		host.Controller.Open(":30");
		host.Controller.Dismiss();

		Assert.AreSequenceEqual(["save", "move:30:1", "restore"], target.Log);
		Assert.AreEqual(12, target.CaretLineNumber);
	}

	[TestMethod]
	public void SetText_ToAnotherMode_PutsTheViewBack()
	{
		FakeEditorNavigationTarget target = DocumentOf();

		var other = new ScriptedModeProvider(">");
		using var host = new QuickPickTestHost(new GoToLineQuickPickProvider(() => target), other);

		host.Controller.Open(":12");
		host.Controller.SetText(">");
		host.PassDebounce();

		Assert.AreSequenceEqual(["save", "move:12:1", "restore"], target.Log);
	}
}
