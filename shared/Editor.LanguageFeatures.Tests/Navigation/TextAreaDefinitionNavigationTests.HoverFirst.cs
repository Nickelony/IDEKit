#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.IntelliSense.Hover;
using Nickelony.IDEKit.IntelliSense.Navigation;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextAreaDefinitionNavigationTests
{
	[TestMethod]
	public void TryGoToDefinition_OffsetOutsideDocument_ReturnsFalse()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1));
		var hoverProvider = new TestHoverProvider(CreateHoverInfo());

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinition(provider, hoverProvider, -1));
			Assert.IsFalse(editor.TextArea.TryGoToDefinition(provider, hoverProvider, editor.Document.TextLength + 1));
			Assert.IsNull(provider.LastRequest);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_HoverWithoutSymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1));
		var hoverProvider = new TestHoverProvider(new TextHoverInfo("Hover text without a symbol."));

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinition(provider, hoverProvider, 0));
			Assert.IsNull(provider.LastRequest);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_NullHoverResult_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(1));
		var hoverProvider = new TestHoverProvider(hoverInfo: null);

		using (hostWindow)
		{
			// A hover provider that resolves no symbol yields no navigation and never asks the definition provider.
			Assert.IsFalse(editor.TextArea.TryGoToDefinition(provider, hoverProvider, 0));
			Assert.IsNull(provider.LastRequest);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_HoveredSymbol_NavigatesToResolvedDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var provider = new TestDefinitionProvider(LocationAt(3, 8));
		var hoverProvider = new TestHoverProvider(CreateHoverInfo());

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(editor.TextArea.TryGoToDefinition(provider, hoverProvider, 0));

			Assert.IsNotNull(provider.LastRequest);
			Assert.AreEqual("charlie", provider.LastRequest.SymbolName);
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_HoverDiscriminator_PassesItToTheDefinitionRequest()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var discriminator = new TestDiscriminator("variable");
		var hoverProvider = new TestHoverProvider(
			new TextHoverInfo("hover") { SymbolName = "charlie", DefinitionDiscriminator = discriminator });
		var provider = new TestDefinitionProvider(LocationAt(3, 8));

		using (hostWindow)
		{
			Assert.IsTrue(editor.TextArea.TryGoToDefinition(provider, hoverProvider, 0));

			// The hover payload's discriminator reaches the definition request.
			Assert.IsNotNull(provider.LastRequest);
			Assert.AreSame(discriminator, provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_HoverWithoutDiscriminator_PassesNullDiscriminator()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var hoverProvider = new TestHoverProvider(
			new TextHoverInfo("hover") { SymbolName = "charlie" });
		var provider = new TestDefinitionProvider(LocationAt(3, 8));

		using (hostWindow)
		{
			Assert.IsTrue(editor.TextArea.TryGoToDefinition(provider, hoverProvider, 0));

			// A hover payload without a discriminator leaves the definition request's discriminator null.
			Assert.IsNotNull(provider.LastRequest);
			Assert.IsNull(provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_HoveredSymbol_NavigatesToResolvedDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionRequest? definitionRequest = null;

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(await editor.TextArea.TryGoToDefinitionAsync(
				(request, cancellationToken) =>
				{
					definitionRequest = request;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(3, 8));
				},
				(request, cancellationToken) => Task.FromResult<TextHoverInfo?>(CreateHoverInfo()),
				0));

			Assert.IsNotNull(definitionRequest);
			Assert.AreEqual("charlie", definitionRequest.SymbolName);
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_OffsetOutsideDocument_ReturnsFalseWithoutCallingResolvers()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool definitionResolverCalled = false;
		bool hoverResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.TryGoToDefinitionAsync(
				(_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				(_, _) =>
				{
					hoverResolverCalled = true;
					return Task.FromResult<TextHoverInfo?>(CreateHoverInfo());
				},
				editor.Document.TextLength + 1);

			// The async form mirrors the sync form's early return: an offset outside the document skips
			// both resolvers.
			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
			Assert.IsFalse(hoverResolverCalled);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_HoverWithoutSymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool definitionResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.TryGoToDefinitionAsync(
				(_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				(_, _) => Task.FromResult<TextHoverInfo?>(new TextHoverInfo("Hover text without a symbol.")),
				0);

			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_NullHoverResult_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool definitionResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.TryGoToDefinitionAsync(
				(_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<TextDefinitionLocation?>(LocationAt(1));
				},
				(_, _) => Task.FromResult<TextHoverInfo?>(null),
				0);

			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => editor.TextArea.TryGoToDefinitionAsync(
				(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
				(_, _) => Task.FromResult<TextHoverInfo?>(CreateHoverInfo()),
				0,
				cancellationToken: cancellationTokenSource.Token));
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_NullResolvers_ThrowArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => editor.TextArea.TryGoToDefinitionAsync(
				null!,
				(_, _) => Task.FromResult<TextHoverInfo?>(CreateHoverInfo()),
				0));

			await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => editor.TextArea.TryGoToDefinitionAsync(
				(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(1)),
				null!,
				0));
		}
	}

	[TestMethod]
	public void TryGoToDefinitionAsync_PoolThreadHoverContinuation_NavigatesOnTheEditorThread()
	{
		int dispatcherThreadId = Environment.CurrentManagedThreadId;
		int caretThreadId = 0;
		var hoverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var hoverResolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			editor.TextArea.Caret.PositionChanged += (_, _) => caretThreadId = Environment.CurrentManagedThreadId;

			// The test thread carries no synchronization context, so the hover resolver's continuation
			// resumes on a thread-pool thread; the definition request and the applied navigation must still
			// run on the editor thread, which the caret event records.
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionAsync(
				(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(3, 8)),
				async (_, _) =>
				{
					hoverResolved.TrySetResult();
					await hoverGate.Task.ConfigureAwait(false);
					return CreateHoverInfo();
				},
				0);

			// Wait on the resolver's start signal instead of a fixed pump window, so the gate is released only
			// once the hover resolution is genuinely in flight.
			DispatcherTestUtils.PumpUntil(() => hoverResolved.Task.IsCompleted);
			hoverGate.SetResult();
			DispatcherTestUtils.PumpUntil(() => navigation.IsCompleted);

			Assert.IsTrue(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(dispatcherThreadId, caretThreadId, "Navigation must be applied on the editor thread.");
		}
	}

	[TestMethod]
	public void TryGoToDefinition_CrossFileLocation_InvokesTheCrossFileCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation? forwarded = null;

		using (hostWindow)
		{
			TextDefinitionLocation location = LocationAt(1, documentId: "other.txt");

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			bool navigated = editor.TextArea.TryGoToDefinition(
				new TestDefinitionProvider(location),
				new TestHoverProvider(CreateHoverInfo()),
				0,
				candidate =>
				{
					forwarded = candidate;
					return true;
				});

			// The hover-first form shares the cross-file delegation with the other entry points.
			Assert.IsTrue(navigated);
			Assert.AreSame(location, forwarded);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public void TryGoToDefinition_CrossFileLocationWithoutCallback_ReturnsFalse()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.IsFalse(editor.TextArea.TryGoToDefinition(
				new TestDefinitionProvider(LocationAt(1, documentId: "other.txt")),
				new TestHoverProvider(CreateHoverInfo()),
				0));
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_CrossFileLocation_InvokesTheCrossFileCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		TextDefinitionLocation? forwarded = null;

		using (hostWindow)
		{
			TextDefinitionLocation location = LocationAt(1, documentId: "other.txt");

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			bool navigated = await editor.TextArea.TryGoToDefinitionAsync(
				(_, _) => Task.FromResult<TextDefinitionLocation?>(location),
				(_, _) => Task.FromResult<TextHoverInfo?>(CreateHoverInfo()),
				0,
				(candidate, _) =>
				{
					forwarded = candidate;
					return Task.FromResult(true);
				});

			Assert.IsTrue(navigated);
			Assert.AreSame(location, forwarded);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task TryGoToDefinitionAsync_DocumentEditedWhileHoverResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var hoverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.TryGoToDefinitionAsync(
				(_, _) => Task.FromResult<TextDefinitionLocation?>(LocationAt(2, 8)),
				async (_, _) =>
				{
					await hoverGate.Task.ConfigureAwait(true);
					return CreateHoverInfo();
				},
				0);

			// The document changes while the hover resolver is suspended; the definition that was resolved
			// from the previous snapshot must not be applied to the edited text.
			editor.Document.Insert(editor.Document.TextLength, " edited");
			hoverGate.SetResult();

			DispatcherTestUtils.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}
}
