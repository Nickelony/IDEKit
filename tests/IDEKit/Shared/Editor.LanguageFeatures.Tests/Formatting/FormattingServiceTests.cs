#if AVALONIAEDIT
using AvaloniaEdit;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Formatting;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Editing;
using Nickelony.IDEKit.LanguageFeatures.Formatting;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

/// <summary>Verifies that a formatting provider's line and character edits are validated as one batch and applied to the editor's document.</summary>
/// <remarks>
/// The editors are never shown in a window, so the class also runs in a headless leg.
/// </remarks>
[STATestClass]
public sealed class FormattingServiceTests
{
	private const string DocumentId = "formatting-test.lua";

	private static readonly FormattingOptions s_options = new(tabSize: 4, insertSpaces: true);

	[TestMethod]
	public async Task FormatDocumentAsync_ProviderEdits_AreAppliedAsOneUndoStep()
	{
		TextEditor editor = TestHost.CreateEditor("local a = 1   \nlocal b = 2  \nlocal c = 3");
		editor.Document.UndoStack.ClearAll();

		var provider = new ScriptedFormattingProvider(_ =>
			CreateEdit(
				new TextEdit(new TextPositionRange(new TextPosition(0, 11), new TextPosition(0, 14)), string.Empty),
				new TextEdit(new TextPositionRange(new TextPosition(1, 11), new TextPosition(1, 13)), string.Empty)
			)
		);

		FormattingOutcome outcome = await new FormattingService().FormatDocumentAsync(editor, provider, DocumentId, options: s_options);

		Assert.AreEqual(FormattingOutcome.Applied, outcome);
		Assert.AreEqual("local a = 1\nlocal b = 2\nlocal c = 3", editor.Text);

		editor.Undo();
		Assert.AreEqual("local a = 1   \nlocal b = 2  \nlocal c = 3", editor.Text);
	}

	[TestMethod]
	public async Task FormatDocumentAsync_SendsTheDocumentTextAndTheOptions()
	{
		TextEditor editor = TestHost.CreateEditor("print(1)");
		FormattingRequest? seen = null;

		var provider = new ScriptedFormattingProvider(request =>
		{
			seen = request;
			return null;
		});

		await new FormattingService().FormatDocumentAsync(editor, provider, DocumentId, options: s_options);

		Assert.IsNotNull(seen);
		Assert.AreEqual(DocumentId, seen.DocumentId);
		Assert.AreEqual("print(1)", seen.DocumentText);
		Assert.AreSame(s_options, seen.Options);
	}

	[TestMethod]
	public async Task FormatDocumentAsync_EditsBeforeTheCaret_MoveTheCaretWithItsText()
	{
		TextEditor editor = TestHost.CreateEditor("a   \nbcd");
		editor.CaretOffset = editor.Document.GetOffset(2, 3);

		var provider = new ScriptedFormattingProvider(_ =>
			CreateEdit(new TextEdit(new TextPositionRange(new TextPosition(0, 1), new TextPosition(0, 4)), string.Empty))
		);

		await new FormattingService().FormatDocumentAsync(editor, provider, DocumentId, options: s_options);

		Assert.AreEqual("a\nbcd", editor.Text);
		Assert.AreEqual(editor.Document.GetOffset(2, 3), editor.CaretOffset);
	}

	[TestMethod]
	public async Task FormatDocumentAsync_DocumentChangedWhileTheProviderWorked_IsStaleAndKeepsTheUsersText()
	{
		TextEditor editor = TestHost.CreateEditor("print(1)");

		var provider = new ScriptedFormattingProvider(_ =>
		{
			editor.Document.Insert(0, "-- typed meanwhile\n");
			return CreateEdit(new TextEdit(new TextPositionRange(new TextPosition(0, 0), new TextPosition(0, 5)), "PRINT"));
		});

		FormattingOutcome outcome = await new FormattingService().FormatDocumentAsync(editor, provider, DocumentId, options: s_options);

		Assert.AreEqual(FormattingOutcome.Stale, outcome);
		Assert.AreEqual("-- typed meanwhile\nprint(1)", editor.Text);
	}

	[TestMethod]
	public async Task FormatDocumentAsync_NullArguments_Throw()
	{
		TextEditor editor = TestHost.CreateEditor("print(1)");

		var service = new FormattingService();
		var provider = new ScriptedFormattingProvider(_ => null);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			service.FormatDocumentAsync(editor: null!, provider, DocumentId, options: s_options)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
			service.FormatDocumentAsync(editor, provider: null!, DocumentId, options: s_options)
		);

		await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => service.FormatDocumentAsync(editor, provider, DocumentId, options: null!));
		await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.FormatDocumentAsync(editor, provider, documentId: " ", options: s_options));
	}

	private static WorkspaceEdit CreateEdit(params TextEdit[] edits) => new([new TextDocumentEdit(DocumentId, edits)]);

	private sealed class ScriptedFormattingProvider(Func<FormattingRequest, WorkspaceEdit?> answer) : IFormattingProvider
	{
		public Task<WorkspaceEdit?> GetFormattingEditsAsync(FormattingRequest request, CancellationToken cancellationToken = default) =>
			Task.FromResult(answer(request));
	}
}
