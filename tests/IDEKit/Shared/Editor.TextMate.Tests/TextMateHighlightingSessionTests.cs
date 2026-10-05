#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using static Nickelony.IDEKit.AvaloniaEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using static Nickelony.IDEKit.AvalonEdit.TextMate.Tests.TextMateRunStyleTestHelpers;
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
		var layered = new NoOpTransformer();

		editor.TextArea.TextView.LineTransformers.Add(layered);

		var session = TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), new TextMateTokenTheme());

		try
		{
			// The syntax colors are the bottom layer, so a transformer added earlier still paints over them.
			Assert.AreSame(session.Colorizer, editor.TextArea.TextView.LineTransformers[0]);
			Assert.IsGreaterThan(0, editor.TextArea.TextView.LineTransformers.IndexOf(layered));

			// Tokenization starts because the colorizer registers itself as a model listener.
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
		var logger = new RecordingLogger();

		var session = TextMateHighlightingSession.Start(
			textView: editor.TextArea.TextView,
			grammar: CreateGrammar("lua"),

			theme: CreateTheme(
				new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" },
				new TextMateTokenThemeRule { Scope = "string", Foreground = "not-a-color" }
			),

			logger
		);

		try
		{
			// The exposed resolver resolves against the supplied theme and reports malformed rules
			// through the supplied logger.
			Assert.AreEqual("#FF123456", GetForegroundColor(session.StyleResolver.Resolve(["source.lua", "keyword.control.lua"])));
			Assert.Contains(message => message.Contains("not-a-color", StringComparison.Ordinal), logger.Messages);
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
		var session = TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), new TextMateTokenTheme());

		session.Dispose();

		// Disposing the model also stops its tokenizer thread and disposes the line list.
		Assert.IsTrue(session.Model.IsStopped);
		Assert.DoesNotContain(session.Colorizer, editor.TextArea.TextView.LineTransformers);

		// The disposal is idempotent.
		session.Dispose();
	}

	[TestMethod]
	public void Theme_RecolorsWithoutTokenizingAgainOrMovingTheColorizer()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };

		var session = TextMateHighlightingSession.Start(
			editor.TextArea.TextView,
			CreateGrammar("lua"),
			CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#123456" })
		);

		try
		{
			editor.TextArea.TextView.LineTransformers.Add(new NoOpTransformer());
			TextMateTokenTheme dark = CreateTheme(new TextMateTokenThemeRule { Scope = "keyword", Foreground = "#654321" });

			session.Theme = dark;

			Assert.AreSame(dark, session.Theme);
			Assert.AreEqual("#FF654321", GetForegroundColor(session.StyleResolver.Resolve(["source.lua", "keyword.control.lua"])));
			Assert.AreSame(session.Colorizer, editor.TextArea.TextView.LineTransformers[0]);
			Assert.IsFalse(session.Model.IsStopped);
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void Dispose_ThenDocumentEdits_LeaveTheSnapshotDetached()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var session = TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), new TextMateTokenTheme());

		session.Dispose();

		// The line list detached from the document, so later edits do not reach it; its snapshot
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
		var session = TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), new TextMateTokenTheme());

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
		Assert.ThrowsExactly<InvalidOperationException>(() =>
			TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), new TextMateTokenTheme())
		);
	}

	[TestMethod]
	public void Start_NullArgument_ThrowsArgumentNullException()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextMateHighlightingSession.Start(textView: null!, CreateGrammar("lua"), new TextMateTokenTheme())
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextMateHighlightingSession.Start(editor.TextArea.TextView, grammar: null!, new TextMateTokenTheme())
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), theme: null!)
		);
	}

	[TestMethod]
	public void Theme_SetToNull_ThrowsAndKeepsTheCurrentTheme()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };
		var theme = new TextMateTokenTheme();
		var session = TextMateHighlightingSession.Start(editor.TextArea.TextView, CreateGrammar("lua"), theme);

		try
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => session.Theme = null!);
			Assert.AreSame(theme, session.Theme);
		}
		finally
		{
			session.Dispose();
		}
	}

	[TestMethod]
	public void Start_WhenSetupFailsAfterResourceAllocation_DisposesTheCreatedResources()
	{
		var editor = new TextEditor { Document = new TextDocument("local value = 1\n") };

		int editorTransformerCount = editor.TextArea.TextView.LineTransformers.Count;
		TextMateDocumentLineList? createdLineList = null;

		// The hook fails inside the window between resource creation and grammar application, which is the
		// window the rollback covers.
		Assert.ThrowsExactly<InvalidOperationException>(() =>
			TextMateHighlightingSession.Start(
				textView: editor.TextArea.TextView,
				grammar: CreateGrammar("lua"),
				theme: new TextMateTokenTheme(),
				logger: null,

				testHooks: new TextMateHighlightingSessionTestHooks
				{
					LineListCreated = lineList =>
					{
						createdLineList = lineList;
						throw new InvalidOperationException("Simulated failure after the resources were created.");
					},
				}
			)
		);

		Assert.IsNotNull(createdLineList);

		// The rollback disposed the model and, with it, the line list, so the list detached from the
		// document and later edits do not reach its snapshot.
		int lineCountBeforeTheEdit = createdLineList.GetNumberOfLines();

		editor.Document.Insert(0, "-- comment\n");

		Assert.AreEqual(lineCountBeforeTheEdit, createdLineList.GetNumberOfLines());
		Assert.AreEqual("local value = 1\n", createdLineList.GetLineTextIncludingTerminators(0).ToString());
		Assert.HasCount(editorTransformerCount, editor.TextArea.TextView.LineTransformers);
	}

	private sealed class NoOpTransformer : DocumentColorizingTransformer
	{
		protected override void ColorizeLine(DocumentLine line) { }
	}
}
