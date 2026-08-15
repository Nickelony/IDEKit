#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateThemeTestHelpers;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.TextMate.Highlighting;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateThemeTestHelpers;
#endif

#if AVALONIAEDIT
namespace Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests;
#else
namespace Nickelony.IDEKit.AvalonEdit.TextMate.Tests;
#endif

[STATestClass]
public sealed class TextMateHighlightingSessionTests
{
	[TestMethod]
	public void Start_CreatesResourcesAndStartsTokenization()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme());

		try
		{
			// The session never installs itself: the host adds the transformer to the view.
			Assert.IsFalse(editor.TextArea.TextView.LineTransformers.Contains(session.Transformer));

			// Tokenization starts because the transformer registers itself as a model listener.
			Assert.IsFalse(session.Model.IsStopped);
			WaitForTokenization(session.Model, lineIndex: 0);

			Assert.AreEqual(editor.Document.LineCount, session.LineList.GetNumberOfLines());
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void Start_ResolverUsesTheSuppliedThemeAndLogger()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var logger = new CapturingLogger();
		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
				new TextMateTokenThemeRule { Scope = "string", Foreground = "not-a-color" }),
			logger);

		try
		{
			// The exposed resolver resolves against the supplied theme and reports malformed rules
			// through the supplied logger.
			Assert.AreEqual("#FF123456", GetForegroundColor(session.StyleResolver.Resolve(["source.lua", "keyword.control.lua"])));
			Assert.IsTrue(logger.Messages.Any(message => message.Contains("not-a-color", StringComparison.Ordinal)));
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void Dispose_DetachesTransformerAndDisposesModel()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme());

		session.Dispose();

		// Disposing the model also stops its tokenizer thread and disposes the line list.
		Assert.IsTrue(session.Model.IsStopped);

		// The disposal is idempotent.
		session.Dispose();
	}

	[TestMethod]
	public void Dispose_ThenDocumentEdits_LeaveTheSnapshotDetached()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme());

		session.Dispose();

		// The line list detached from the document, so later edits no longer reach it; its snapshot
		// stays at the last tracked state of the document.
		editor.Document.Insert(0, "-- comment\n");
		editor.Document.Text = "x";

		Assert.AreEqual(2, session.LineList.GetNumberOfLines());
		Assert.AreEqual("local value = 1\n", session.LineList.GetLineTextIncludingTerminators(0).ToString());
	}

	[TestMethod]
	public void DocumentSwap_DoesNotRebindTheSession()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme());

		try
		{
			editor.Document = new TextDocument("local other = 2\n");
			editor.Document.Insert(0, "-- comment\n");

			// Following a document swap is host policy: the session keeps tracking the document it was
			// started with until the host disposes it and starts a new one.
			Assert.AreEqual(2, session.LineList.GetNumberOfLines());
			Assert.AreEqual("local value = 1\n", session.LineList.GetLineTextIncludingTerminators(0).ToString());
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void Start_WithoutDocument_Throws()
	{
		var editor = new TextEditor { Document = null };

		// The session tokenizes the document the view renders, so a view without a document cannot
		// start one; the misuse is rejected instead of producing a session that never colorizes.
		Assert.ThrowsExactly<InvalidOperationException>(() => TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme()));
	}

	[TestMethod]
	public void Start_WhenSetupFailsAfterResourceAllocation_DisposesTheCreatedResources()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		TextMateDocumentLineList? createdLineList = null;

		// The hook fails inside the window between resource creation and grammar application, which is the
		// window the rollback covers.
		Assert.ThrowsExactly<InvalidOperationException>(() => TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			new TextMateTokenTheme(),
			logger: null,
			testHooks: new TextMateHighlightingSessionTestHooks
			{
				LineListCreated = lineList =>
				{
					createdLineList = lineList;
					throw new InvalidOperationException("Simulated failure after the resources were created.");
				}
			}));

		Assert.IsNotNull(createdLineList);

		// The rollback disposed the model and, with it, the line list, so the list detached from the
		// document and later edits no longer reach its snapshot.
		int lineCountBeforeTheEdit = createdLineList.GetNumberOfLines();

		editor.Document.Insert(0, "-- comment\n");

		Assert.AreEqual(lineCountBeforeTheEdit, createdLineList.GetNumberOfLines());
		Assert.AreEqual("local value = 1\n", createdLineList.GetLineTextIncludingTerminators(0).ToString());
	}
}
