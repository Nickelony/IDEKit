#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextAreaDefinitionNavigationTests
{
	[TestMethod]
	public void TryGoToDefinitionBySymbol_LocationWithoutFilePath_PlacesCaretInColumnWithoutSelecting()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", null));

			// The caret is placed in the requested column without leaving the line selected.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(editor.CaretOffset, editor.SelectionStart);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_LocationWithSelectionRange_PlacesCaretAtSelectionStart()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(targetRange, selectionRange: selectionRange));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", null));

			// The selection range identifies the symbol name inside the target range.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_ColumnBeyondLineEnd_ClampsCaretToLineEnd()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(3, 200));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "charlie", null));

			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_ColumnNearIntMax_ClampsCaretToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, int.MaxValue));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", null));

			// A column that would overflow the offset addition must still clamp to the line end.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_LocationBeyondLastLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(99));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "alpha", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_CrossFileLocationWithoutCallback_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1, 1, @"C:\Sources\source.txt"));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "source", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_CrossFileLocationWithCallback_DelegatesAndPropagatesResult()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation location = LocationAt(4, 9, @"C:\Sources\source.txt");
		var provider = new TestDefinitionProvider(location);
		TextDefinitionLocation? delegatedLocation = null;
		bool callbackResult = false;

		bool TryNavigateCrossFile(TextDefinitionLocation target)
		{
			delegatedLocation = target;
			return callbackResult;
		}

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "source", null, TryNavigateCrossFile));
			Assert.AreSame(location, delegatedLocation);
			Assert.AreEqual(0, editor.CaretOffset);

			callbackResult = true;

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "source", null, TryNavigateCrossFile));
			Assert.AreSame(location, delegatedLocation);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_EmptySymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "  ", null));
			Assert.IsNull(provider.LastRequest);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_NonNullDiscriminator_ForwardsItToTheProvider()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var discriminator = new TestDiscriminator("kind");
		var provider = new TestDefinitionProvider(LocationAt(2, 8));

		using (hostWindow)
		{
			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", discriminator));

			// The language-specific discriminator reaches the provider unchanged, so a provider can
			// disambiguate identical symbol names.
			Assert.IsNotNull(provider.LastRequest);
			Assert.AreSame(discriminator, provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_NullProvider_ThrowsArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => editor.TextArea.TryGoToDefinitionBySymbol(null!, "alpha"));
			Assert.ThrowsExactly<ArgumentNullException>(() => TextAreaDefinitionNavigation.TryGoToDefinitionBySymbol(null!, new TestDefinitionProvider(null), "alpha"));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_OffEditorThread_ThrowsInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(2, 8));
		Exception? captured = null;

		using (hostWindow)
		{
			// The synchronous form applies navigation on the calling thread and verifies the editor thread
#if AVALONIAEDIT
			// up front, so calling it elsewhere fails fast instead of corrupting editor state. Avalonia
			// divergence: a Task.Run work item can run inline on the headless dispatcher thread, so a dedicated
			// thread is used to actually leave the editor thread before the affinity check runs.
			var thread = new Thread(() =>
#else
			// up front, so calling it elsewhere fails fast instead of corrupting editor state.
			Task.Run(() =>
#endif
			{
				try
				{
					editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo");
				}
				catch (Exception exception)
				{
					captured = exception;
				}
#if AVALONIAEDIT
			});

			thread.Start();
			thread.Join();
#else
			}).GetAwaiter().GetResult();
#endif

			Assert.IsInstanceOfType<InvalidOperationException>(captured);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_NegativeLine_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(-1, 0), new TextPosition(-1, 0))));

		using (hostWindow)
		{
			// A negative line cannot identify a location and must be rejected instead of clamping to line 1.
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "alpha", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_NegativeCharacter_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(1, -5), new TextPosition(1, -5))));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", null));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionBySymbol_CharacterAtIntMaxValue_SaturatesToLineEndWithoutOverflow()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(new TextDefinitionLocation(
			new TextPositionRange(new TextPosition(1, int.MaxValue), new TextPosition(1, int.MaxValue))));

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(editor.TextArea.TryGoToDefinitionBySymbol(provider, "bravo", null));

			// The one-based column addition saturates, so the caret clamps to the line end instead of
			// overflowing into a negative column.
			Assert.AreEqual(line.EndOffset, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_LocationWithSelectionRange_PlacesCaretAtSelectionStart()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var targetRange = new TextPositionRange(new TextPosition(1, 0), new TextPosition(1, 15));
		var selectionRange = new TextPositionRange(new TextPosition(1, 7), new TextPosition(1, 12));
		var location = new TextDefinitionLocation(targetRange, selectionRange: selectionRange);

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(await editor.TextArea.TryGoToDefinitionBySymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"bravo"));

			// The asynchronous form resolves through the callback and navigates like the synchronous one.
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_CrossFileLocationWithCallback_DelegatesAndPropagatesResult()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation location = LocationAt(4, 9, @"C:\Sources\source.txt");
		TextDefinitionLocation? delegatedLocation = null;
		bool callbackResult = false;

		using (hostWindow)
		{
			Assert.IsFalse(await editor.TextArea.TryGoToDefinitionBySymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"source",
				tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) =>
				{
					delegatedLocation = target;
					return Task.FromResult(callbackResult);
				}));

			Assert.AreSame(location, delegatedLocation);

			callbackResult = true;

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			Assert.IsTrue(await editor.TextArea.TryGoToDefinitionBySymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(location),
				"source",
				tryNavigateCrossFileDefinitionAsync: (target, cancellationToken) => Task.FromResult(callbackResult)));

			Assert.AreSame(location, delegatedLocation);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_DocumentEditedWhileResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var resolverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionBySymbolAsync(
				async (_, _) =>
				{
					await resolverGate.Task.ConfigureAwait(true);
					return LocationAt(2, 8);
				},
				"bravo");

			// The document changes while the resolver is suspended; the location was resolved from the
			// previous snapshot, so it must not be applied to the edited text.
			editor.Document.Insert(editor.Document.TextLength, " edited");
			resolverGate.SetResult();

			DispatcherTestUtils.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_EmptySymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool requested = false;

		using (hostWindow)
		{
			Assert.IsFalse(await editor.TextArea.TryGoToDefinitionBySymbolAsync(
				(request, cancellationToken) =>
				{
					requested = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				"  "));

			Assert.IsFalse(requested);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.TextArea.TryGoToDefinitionBySymbolAsync(
				(request, cancellationToken) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
				"alpha",
				cancellationToken: cancellationTokenSource.Token));
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_NullResolver_Throws()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(
				() => editor.TextArea.TryGoToDefinitionBySymbolAsync(null!, "alpha"));
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionBySymbolAsync_OffEditorThread_FaultsWithInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		Exception? captured = null;

		using (hostWindow)
		{
			// The asynchronous form also verifies the editor thread before doing any work; the failure is
#if AVALONIAEDIT
			// reported through the returned task. Avalonia divergence: a Task.Run work item can run inline on
			// the headless dispatcher thread, so a dedicated thread is used to actually leave the editor
			// thread before the affinity check runs.
			var thread = new Thread(() =>
#else
			// reported through the returned task.
			Task.Run(() =>
#endif
			{
				try
				{
					editor.TextArea.TryGoToDefinitionBySymbolAsync(
						(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(2, 8)),
						"bravo").GetAwaiter().GetResult();
				}
				catch (Exception exception)
				{
					captured = exception;
				}
#if AVALONIAEDIT
			});

			thread.Start();
			thread.Join();
#else
			}).GetAwaiter().GetResult();
#endif

			Assert.IsInstanceOfType<InvalidOperationException>(captured);
		}
	}
}
