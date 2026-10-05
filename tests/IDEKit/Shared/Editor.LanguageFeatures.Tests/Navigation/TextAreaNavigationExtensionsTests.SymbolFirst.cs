#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class TextAreaNavigationExtensionsTests
{
	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_LocationWithoutFilePath_PlacesCaretInColumnWithoutSelecting()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "bravo"));

			// The caret is placed in the requested column without leaving the line selected.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(editor.CaretOffset, editor.SelectionStart);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_ColumnBeyondLineEnd_ClampsCaretToLineEnd()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(3, 200));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "charlie"));

			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_ColumnNearIntMax_ClampsCaretToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(2, int.MaxValue));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "bravo"));

			// A column that would overflow the offset addition must still clamp to the line end.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_LocationBeyondLastLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(99));

		using (hostWindow)
		{
			Assert.IsFalse(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "alpha"));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_CrossFileLocationWithoutCallback_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new StubDefinitionProvider(LocationAt(1, 1, @"C:\Sources\source.txt"));

		using (hostWindow)
		{
			Assert.IsFalse(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "source"));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_NonNullDiscriminator_ForwardsItToTheProvider()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var discriminator = new TestDiscriminator("kind");
		var provider = new StubDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			Assert.IsTrue(
				await GoToDefinitionBySymbolAsync(textArea: editor.TextArea, definitionProvider: provider, symbolName: "bravo", discriminator)
			);

			// The language-specific discriminator reaches the provider unchanged, so a provider can
			// disambiguate identical symbol names.
			Assert.IsNotNull(provider.LastRequest);
			Assert.AreSame(discriminator, provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_NegativeLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var provider = new StubDefinitionProvider(new DefinitionLocation(new TextPositionRange(new TextPosition(-1, 0), new TextPosition(-1, 0))));

		using (hostWindow)
		{
			// A negative line cannot identify a location and must be rejected instead of clamping to line 1.
			Assert.IsFalse(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "alpha"));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_NegativeCharacter_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var provider = new StubDefinitionProvider(new DefinitionLocation(new TextPositionRange(new TextPosition(1, -5), new TextPosition(1, -5))));

		using (hostWindow)
		{
			Assert.IsFalse(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "bravo"));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_CharacterAtIntMaxValue_SaturatesToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var provider = new StubDefinitionProvider(
			new DefinitionLocation(new TextPositionRange(new TextPosition(1, int.MaxValue), new TextPosition(1, int.MaxValue)))
		);

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(await GoToDefinitionBySymbolAsync(editor.TextArea, provider, "bravo"));

			// The one-based column addition saturates, so the caret clamps to the line end instead of
			// overflowing into a negative column.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_LocationWithSelectionRange_PlacesCaretAtSelectionStart()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var location = new DefinitionLocation(targetRange, selectionRange: selectionRange);

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(
				await editor.TextArea.GoToDefinitionBySymbolAsync(
					DocumentId,
					(request, cancellationToken) => Task.FromResult<DefinitionLocation?>(location),
					"bravo"
				)
			);

			// The target is placed at the start of its selection range, not at the start of the target range.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_CrossFileLocationWithCallback_DelegatesAndPropagatesResult()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		DefinitionLocation location = LocationAt(4, 9, @"C:\Sources\source.txt");
		DefinitionLocation? delegatedLocation = null;
		bool callbackResult = false;

		using (hostWindow)
		{
			Assert.IsFalse(
				await editor.TextArea.GoToDefinitionBySymbolAsync(
					DocumentId,
					definitionResolverAsync: (request, cancellationToken) => Task.FromResult<DefinitionLocation?>(location),
					symbolName: "source",

					tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) =>
					{
						delegatedLocation = target;
						return Task.FromResult(callbackResult);
					}
				)
			);

			Assert.AreSame(location, delegatedLocation);

			callbackResult = true;

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			Assert.IsTrue(
				await editor.TextArea.GoToDefinitionBySymbolAsync(
					DocumentId,
					definitionResolverAsync: (request, cancellationToken) => Task.FromResult<DefinitionLocation?>(location),
					symbolName: "source",
					tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) => Task.FromResult(callbackResult)
				)
			);

			Assert.AreSame(location, delegatedLocation);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_DocumentEditedWhileResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var resolverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.GoToDefinitionBySymbolAsync(
				DocumentId,

				async (_, _) =>
				{
					await resolverGate.Task.ConfigureAwait(true);
					return LocationAt(2, 8);
				},

				"bravo"
			);

			// The document changes while the resolver is suspended; the location was resolved from the
			// previous snapshot, so it must not be applied to the edited text.
			editor.Document.Insert(editor.Document.TextLength, " edited");
			resolverGate.SetResult();

			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_EmptySymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool requested = false;

		using (hostWindow)
		{
			Assert.IsFalse(
				await editor.TextArea.GoToDefinitionBySymbolAsync(
					DocumentId,

					(request, cancellationToken) =>
					{
						requested = true;
						return Task.FromResult<DefinitionLocation?>(LocationAt(1));
					},

					"  "
				)
			);

			Assert.IsFalse(requested);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
				editor.TextArea.GoToDefinitionBySymbolAsync(
					DocumentId,
					definitionResolverAsync: (request, cancellationToken) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
					symbolName: "alpha",
					cancellationToken: cancellationTokenSource.Token
				)
			);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_NullResolver_Throws()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => editor.TextArea.GoToDefinitionBySymbolAsync(DocumentId, null!, "alpha"));
	}

	[TestMethod]
	public async Task GoToDefinitionBySymbolAsync_OffEditorThread_FaultsWithInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			// The asynchronous form also verifies the editor thread before doing any work; the failure is
			// reported through the returned task, and a dedicated thread leaves the editor thread before the
			// affinity check runs.
			Exception? captured = TestThreads.CaptureFailure(() =>
				editor
					.TextArea.GoToDefinitionBySymbolAsync(DocumentId, (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(2, 8)), "bravo")
					.GetAwaiter()
					.GetResult()
			);

			Assert.IsInstanceOfType<InvalidOperationException>(captured);
		}
	}
}
