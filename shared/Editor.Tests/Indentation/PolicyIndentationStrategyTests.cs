#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.Indentation;
using Nickelony.IDEKit.Core.Indentation;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.Indentation;
using Nickelony.IDEKit.Core.Indentation;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.Tests;
#endif

[TestClass]
public sealed class PolicyIndentationStrategyTests
{
	private sealed class TestPolicy : IIndentationPolicy
	{
		public string GetDesiredIndentation(in IndentationContext context)
		{
			string indentation = IndentationOperations.GetLeadingWhitespace(context.PreviousLineText);

			if (context.UseSmartIndent && context.PreviousLineText.TrimEnd().EndsWith("open"))
				indentation += context.IndentationUnit;

			if (context.UseSmartIndent && context.CurrentLineText.TrimStart().StartsWith("close"))
				indentation = IndentationOperations.TruncateIndentationByUnitLength(indentation, context.IndentationUnit);

			return indentation;
		}
	}

	[TestMethod]
	public void IndentLine_OptionChange_IsHonored()
	{
		var options = new TextEditorOptions
		{
			ConvertTabsToSpaces = true,
			IndentationSize = 4
		};

		var strategy = new PolicyIndentationStrategy(options, new TestPolicy());
		var document = new TextDocument("if value open\r\n    ");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value open\r\n    ", document.Text);

		options.IndentationSize = 2;

		strategy.IndentLine(document, document.GetLineByNumber(2));

		// The indentation unit is read from the options on every call, so the change takes effect immediately.
		Assert.AreEqual("if value open\r\n  ", document.Text);
	}

	private static PolicyIndentationStrategy CreateStrategy(Func<TextDocument, DocumentLine, bool>? shouldUseSmartIndent = null) => new(
		new TextEditorOptions
		{
			ConvertTabsToSpaces = true,
			IndentationSize = 4
		},
		new TestPolicy(),
		shouldUseSmartIndent);

	[TestMethod]
	public void IndentLine_AfterOpenToken_AddsIndentToNewLine()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value open\r\n");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value open\r\n    ", document.Text);
	}

	[TestMethod]
	public void IndentLine_BeforeCloseToken_DedentsCurrentLineWithoutExtraInsertion()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value open\r\n close");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value open\r\nclose", document.Text);
	}

	[TestMethod]
	public void IndentLine_AlreadyCorrect_DoesNotModifyDocument()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value open\r\n    ");

		document.UndoStack.ClearAll();
		strategy.IndentLine(document, document.GetLineByNumber(2));

		// An already-correct line skips the replace, so the document and the undo stack stay untouched.
		Assert.AreEqual("if value open\r\n    ", document.Text);
		Assert.IsFalse(document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void IndentLine_SmartIndentPredicateFalse_KeepsPreviousIndentation()
	{
		PolicyIndentationStrategy strategy = CreateStrategy((_, _) => false);
		var document = new TextDocument("if value open\r\n");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		Assert.AreEqual("if value open\r\n", document.Text);
	}

	[TestMethod]
	public void IndentLines_IndentsEachLineInRangeSequentially()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if a open\r\nx\r\nif b open\r\ny");

		strategy.IndentLines(document, 2, 4);

		Assert.AreEqual("if a open\r\n    x\r\n    if b open\r\n        y", document.Text);
	}

	[TestMethod]
	public void IndentLines_ReversedRange_IndentsClampedStartLine()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if a open\r\nx");

		// The end precedes the start; the clamped start line is still indented.
		strategy.IndentLines(document, 2, 1);

		Assert.AreEqual("if a open\r\n    x", document.Text);
	}

	[TestMethod]
	public void IndentLines_EmptyDocument_DoesNothing()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument();

		strategy.IndentLines(document, 1, 10);

		Assert.AreEqual(string.Empty, document.Text);
	}

	[TestMethod]
	public void IndentLine_FirstLine_KeepsExistingIndentationWithoutConsultingThePolicy()
	{
		int predicateCalls = 0;
		PolicyIndentationStrategy strategy = CreateStrategy((_, _) =>
		{
			predicateCalls++;
			return true;
		});

		var document = new TextDocument("   first\r\nsecond");

		strategy.IndentLine(document, document.GetLineByNumber(1));

		// The first line has no previous line, so its leading whitespace is kept and the
		// smart-indent predicate is not consulted.
		Assert.AreEqual("   first\r\nsecond", document.Text);
		Assert.AreEqual(0, predicateCalls);
	}

	[TestMethod]
	public void IndentLine_ConvertTabsToSpacesFalse_UsesTabIndentationUnit()
	{
		var options = new TextEditorOptions
		{
			ConvertTabsToSpaces = false,
			IndentationSize = 4
		};

		var strategy = new PolicyIndentationStrategy(options, new TestPolicy());
		var document = new TextDocument("if value open\r\n");

		strategy.IndentLine(document, document.GetLineByNumber(2));

		// The indentation unit follows the options: a tab is appended when tabs are used.
		Assert.AreEqual("if value open\r\n\t", document.Text);
	}

	[TestMethod]
	public void IndentLine_SmartIndentPredicate_ReceivesTheDocumentAndThePreviousLine()
	{
		TextDocument? receivedDocument = null;
		DocumentLine? receivedLine = null;

		PolicyIndentationStrategy strategy = CreateStrategy((document, line) =>
		{
			receivedDocument = document;
			receivedLine = line;
			return true;
		});

		var editorDocument = new TextDocument("if value open\r\n");

		strategy.IndentLine(editorDocument, editorDocument.GetLineByNumber(2));

		// The predicate decides smart-indent usage for the line being indented, so it receives the
		// line being indented and the line immediately before it.
		Assert.AreSame(editorDocument, receivedDocument);
		Assert.IsNotNull(receivedLine);
		Assert.AreEqual(1, receivedLine.LineNumber);
	}

	[TestMethod]
	public void IndentLines_OverRangeRequest_ClampsToTheDocument()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();

		var document = new TextDocument("if value open\r\nx");

		strategy.IndentLines(document, 2, 99);

		Assert.AreEqual("if value open\r\n    x", document.Text);

		// A start beyond the document indents the last line.
		var secondDocument = new TextDocument("if value open\r\nx");

		strategy.IndentLines(secondDocument, 99, 99);

		Assert.AreEqual("if value open\r\n    x", secondDocument.Text);
	}

	[TestMethod]
	public void IndentLines_ChangesAreGroupedIntoOneUndoStep()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if a open\r\nx\r\nif b open\r\ny");

		document.UndoStack.ClearAll();

		strategy.IndentLines(document, 2, 4);

		Assert.IsTrue(document.UndoStack.CanUndo);

		// The range replacement is one document update, so a single undo restores every line.
		document.UndoStack.Undo();

		Assert.AreEqual("if a open\r\nx\r\nif b open\r\ny", document.Text);
		Assert.IsFalse(document.UndoStack.CanUndo);
	}

	[TestMethod]
	public void IndentLine_LineFromAnotherDocument_Throws()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value open\r\n");
		var otherDocument = new TextDocument("other\r\n");

		Assert.ThrowsExactly<ArgumentException>(() =>
			strategy.IndentLine(document, otherDocument.GetLineByNumber(2)));

		Assert.AreEqual("if value open\r\n", document.Text);
	}

	[TestMethod]
	public void IndentLine_DeletedLine_Throws()
	{
		PolicyIndentationStrategy strategy = CreateStrategy();
		var document = new TextDocument("if value open\r\nline");
		DocumentLine deletedLine = document.GetLineByNumber(2);

		document.Replace(0, document.TextLength, "replaced");

		Assert.IsTrue(deletedLine.IsDeleted);
		Assert.ThrowsExactly<ArgumentException>(() => strategy.IndentLine(document, deletedLine));
	}
}
