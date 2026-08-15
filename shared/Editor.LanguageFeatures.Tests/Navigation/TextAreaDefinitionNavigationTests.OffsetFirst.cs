#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
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
using ICSharpCode.AvalonEdit.Editing;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Navigation;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextAreaDefinitionNavigationTests
{
	[TestMethod]
	public void TryGoToDefinitionAtOffset_ResolvedLocation_NavigatesAndReceivesTheSnapshot()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		ITextSnapshot? resolvedSnapshot = null;
		int? resolvedOffset = null;

		using (hostWindow)
		{
			bool navigated = editor.TextArea.TryGoToDefinitionAtOffset(
				(snapshot, offset) =>
				{
					resolvedSnapshot = snapshot;
					resolvedOffset = offset;
					return LocationAt(2, 8);
				},
				0);

			DocumentLine line = editor.Document.GetLineByNumber(2);

			// The offset-first resolver receives the document snapshot and the zero-based offset, and its
			// location is applied like a symbol-resolved one.
			Assert.IsTrue(navigated);
			Assert.IsNotNull(resolvedSnapshot);
			Assert.AreEqual(DocumentText, resolvedSnapshot.GetText(0, resolvedSnapshot.TextLength));
			Assert.AreEqual(DocumentText.Length, resolvedSnapshot.TextLength);
			Assert.AreEqual(0, resolvedOffset);
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_OffsetAtDocumentEnd_IsAccepted()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			// The offset at the end of the document is a valid position, like in the hover-first form.
			Assert.IsTrue(editor.TextArea.TryGoToDefinitionAtOffset((_, _) => LocationAt(1), editor.Document.TextLength));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_ResolverReturningNull_ReturnsFalseWithoutNavigating()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionAtOffset((_, _) => null, 0));
			Assert.AreEqual(0, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_OffsetOutsideDocument_ReturnsFalseWithoutResolving()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool resolverCalled = false;

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionAtOffset(
				(_, _) =>
				{
					resolverCalled = true;
					return LocationAt(1);
				},
				-1));

			Assert.IsFalse(editor.TextArea.TryGoToDefinitionAtOffset(
				(_, _) =>
				{
					resolverCalled = true;
					return LocationAt(1);
				},
				editor.Document.TextLength + 1));

			Assert.IsFalse(resolverCalled);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_CrossFileLocation_InvokesTheCrossFileCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation? forwarded = null;

		using (hostWindow)
		{
			TextDefinitionLocation location = LocationAt(1, documentId: "other.txt");

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			bool navigated = editor.TextArea.TryGoToDefinitionAtOffset(
				(_, _) => location,
				0,
				candidate =>
				{
					forwarded = candidate;
					return true;
				});

			// A location that names another document is reported through the callback instead of being
			// applied to this editor.
			Assert.IsTrue(navigated);
			Assert.AreSame(location, forwarded);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_CrossFileLocationWithoutCallback_ReturnsFalse()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinitionAtOffset((_, _) => LocationAt(1, documentId: "other.txt"), 0));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_NullResolver_ThrowsArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.ThrowsExactly<ArgumentNullException>(() => editor.TextArea.TryGoToDefinitionAtOffset(null!, 0));
			Assert.ThrowsExactly<ArgumentNullException>(
				() => TextAreaDefinitionNavigation.TryGoToDefinitionAtOffset(null!, (_, _) => null, 0));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_OffEditorThread_ThrowsInvalidOperationException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		Exception? captured = null;

		using (hostWindow)
		{
#if AVALONIAEDIT
			// Avalonia divergence: a Task.Run work item can run inline on the headless dispatcher thread, so a
			// dedicated thread is used to actually leave the editor thread before the affinity check runs.
			var thread = new Thread(() =>
#else
			Task.Run(() =>
#endif
			{
				try
				{
					editor.TextArea.TryGoToDefinitionAtOffset((_, _) => LocationAt(1), 0);
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
	public async Task TryGoToDefinitionAtOffsetAsync_ResolvedLocation_NavigatesAndReceivesTheSnapshot()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		ITextSnapshot? resolvedSnapshot = null;
		int? resolvedOffset = null;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				(snapshot, offset, _) =>
				{
					resolvedSnapshot = snapshot;
					resolvedOffset = offset;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(2, 8));
				},
				0);

			DocumentLine line = editor.Document.GetLineByNumber(2);

			Assert.IsTrue(navigated);
			Assert.IsNotNull(resolvedSnapshot);
			Assert.AreEqual(DocumentText, resolvedSnapshot.GetText(0, resolvedSnapshot.TextLength));
			Assert.AreEqual(0, resolvedOffset);
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_OffsetOutsideDocument_ReturnsFalseWithoutResolving()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool resolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				(_, _, _) =>
				{
					resolverCalled = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				-1);

			Assert.IsFalse(navigated);
			Assert.IsFalse(resolverCalled);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_CrossFileLocation_InvokesTheCrossFileCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation? forwarded = null;

		using (hostWindow)
		{
			TextDefinitionLocation location = LocationAt(1, documentId: "other.txt");

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			bool navigated = await editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				(_, _, _) => Task.FromResult<TextDefinitionLocation?>(location),
				0,
				(_, _) =>
				{
					forwarded = location;
					return Task.FromResult(true);
				});

			Assert.IsTrue(navigated);
			Assert.AreSame(location, forwarded);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_DocumentEditedWhileResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var resolverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				async (_, _, _) =>
				{
					await resolverGate.Task.ConfigureAwait(true);
					return LocationAt(2, 8);
				},
				0);

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
	public async Task TryGoToDefinitionAtOffsetAsync_DocumentReplacedWhileResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var resolverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				async (_, _, _) =>
				{
					await resolverGate.Task.ConfigureAwait(true);
					return LocationAt(2, 8);
				},
				0);

			// Replacing the document instance invalidates the snapshot even though the returned line and
			// column would still fit the new document.
			editor.Document = new TextDocument("other gamma = 4\nother delta = 5\nother epsilon = 6");
			resolverGate.SetResult();

			DispatcherTestUtils.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_NullResolver_ThrowsArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(
				() => editor.TextArea.TryGoToDefinitionAtOffsetAsync(null!, 0));
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(
				() => TextAreaDefinitionNavigation.TryGoToDefinitionAtOffsetAsync(null!, (_, _, _) => Task.FromResult<TextDefinitionLocation?>(null), 0));
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				(_, _, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
				0,
				cancellationToken: cancellationTokenSource.Token));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffset_BareTextAreaHost_AppliesTheLocation()
	{
		// A TextArea-only host - no TextEditor wrapper - can run definition navigation: the helpers
		// read only text-area state and the resolved location is applied to it.
		var textArea = new TextArea
		{
			Document = new TextDocument(DocumentText)
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(textArea);

		bool navigated = textArea.TryGoToDefinitionAtOffset((_, _) => LocationAt(2, 3), 0);

		DocumentLine line = textArea.Document.GetLineByNumber(2);

		Assert.IsTrue(navigated);
		Assert.AreEqual(line.Offset + 2, textArea.Caret.Offset);
		Assert.IsTrue(textArea.Selection.IsEmpty);
	}

	[TestMethod]
	public async Task TryGoToDefinitionAtOffsetAsync_BareTextAreaHost_AppliesTheLocation()
	{
		var textArea = new TextArea
		{
			Document = new TextDocument(DocumentText)
		};

		using HostWindow hostWindow = TestHost.ShowInHostWindow(textArea);

		bool navigated = await textArea.TryGoToDefinitionAtOffsetAsync(
			(_, _, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(3, 5)),
			0);

		DocumentLine line = textArea.Document.GetLineByNumber(3);

		Assert.IsTrue(navigated);
		Assert.AreEqual(line.Offset + 4, textArea.Caret.Offset);
		Assert.IsTrue(textArea.Selection.IsEmpty);
	}

	[TestMethod]
	public void TryGoToDefinitionAtOffsetAsync_PoolThreadResolverContinuation_NavigatesOnTheEditorThread()
	{
		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int caretThreadId = 0;
		var resolverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resolverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			editor.TextArea.Caret.PositionChanged += (_, _) => caretThreadId = Environment.CurrentManagedThreadId;

			// The test thread carries no synchronization context, so a resolver that resumes on a
			// thread-pool thread must still have its location applied on the editor thread; the caret event
			// that fires while the location is applied records the thread that actually navigated.
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionAtOffsetAsync(
				async (_, _, _) =>
				{
					resolverStarted.TrySetResult();
					await resolverGate.Task.ConfigureAwait(false);
					return LocationAt(3, 5);
				},
				0);

			// Wait on the resolver's start signal instead of a fixed pump window, so the gate is released only
			// once the definition resolution is genuinely in flight.
			DispatcherTestUtils.PumpUntil(() => resolverStarted.Task.IsCompleted);
			resolverGate.SetResult();
			DispatcherTestUtils.PumpUntil(() => navigation.IsCompleted);

			Assert.IsTrue(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(dispatcherThreadId, caretThreadId, "Navigation must be applied on the editor thread.");
		}
	}
}
