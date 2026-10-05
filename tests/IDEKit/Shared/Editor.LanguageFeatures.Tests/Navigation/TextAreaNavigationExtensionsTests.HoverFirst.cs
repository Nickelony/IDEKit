#if AVALONIAEDIT
using AvaloniaEdit;
using AvaloniaEdit.Document;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

#else
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Navigation;
using Nickelony.IDEKit.LanguageFeatures.Hover;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

#endif

public sealed partial class TextAreaNavigationExtensionsTests
{
	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_HoverDiscriminator_PassesItToTheDefinitionRequest()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var discriminator = new TestDiscriminator("variable");
		var hoverProvider = new StubHoverProvider(new HoverInformation("hover") { SymbolName = "charlie", DefinitionDiscriminator = discriminator });
		var provider = new StubDefinitionProvider(LocationAt(3, 8));

		using (hostWindow)
		{
			Assert.IsTrue(await GoToDefinitionByHoverAsync(textArea: editor.TextArea, definitionProvider: provider, hoverProvider, offset: 0));

			// The hover payload's discriminator reaches the definition request.
			Assert.IsNotNull(provider.LastRequest);
			Assert.AreSame(discriminator, provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_HoverWithoutDiscriminator_PassesNullDiscriminator()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		var hoverProvider = new StubHoverProvider(new HoverInformation("hover") { SymbolName = "charlie" });
		var provider = new StubDefinitionProvider(LocationAt(3, 8));

		using (hostWindow)
		{
			Assert.IsTrue(await GoToDefinitionByHoverAsync(textArea: editor.TextArea, definitionProvider: provider, hoverProvider, offset: 0));

			// A hover payload without a discriminator leaves the definition request's discriminator null.
			Assert.IsNotNull(provider.LastRequest);
			Assert.IsNull(provider.LastRequest.Discriminator);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_HoveredSymbol_NavigatesToResolvedDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		DefinitionRequest? definitionRequest = null;

		using (hostWindow)
		{
			DocumentLine line = editor.Document.GetLineByNumber(3);

			Assert.IsTrue(
				await editor.TextArea.GoToDefinitionByHoverAsync(
					DocumentId,

					definitionResolverAsync: (request, cancellationToken) =>
					{
						definitionRequest = request;
						return Task.FromResult<DefinitionLocation?>(LocationAt(3, 8));
					},

					hoverResolverAsync: (request, cancellationToken) => Task.FromResult<HoverInformation?>(CreateHoverInfo()),
					offset: 0,
					cancellationToken: TestContext.CancellationToken
				)
			);

			Assert.IsNotNull(definitionRequest);
			Assert.AreEqual("charlie", definitionRequest.SymbolName);
			Assert.AreEqual(line.Offset + 7, editor.CaretOffset);
			Assert.AreEqual(0, editor.SelectionLength);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_OffsetOutsideDocument_ReturnsFalseWithoutCallingResolvers()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		bool definitionResolverCalled = false;
		bool hoverResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,

				definitionResolverAsync: (_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<DefinitionLocation?>(LocationAt(1));
				},

				hoverResolverAsync: (_, _) =>
				{
					hoverResolverCalled = true;
					return Task.FromResult<HoverInformation?>(CreateHoverInfo());
				},

				offset: editor.Document.TextLength + 1,
				cancellationToken: TestContext.CancellationToken
			);

			// The async form mirrors the sync form's early return: an offset outside the document skips
			// both resolvers.
			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
			Assert.IsFalse(hoverResolverCalled);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_HoverWithoutSymbolName_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool definitionResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,

				definitionResolverAsync: (_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<DefinitionLocation?>(LocationAt(1));
				},

				hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(new HoverInformation("Hover text without a symbol.")),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_NullHoverResult_ReturnsFalseWithoutRequestingDefinition()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		bool definitionResolverCalled = false;

		using (hostWindow)
		{
			bool navigated = await editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,

				definitionResolverAsync: (_, _) =>
				{
					definitionResolverCalled = true;
					return Task.FromResult<DefinitionLocation?>(LocationAt(1));
				},

				hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(null),
				offset: 0,
				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsFalse(navigated);
			Assert.IsFalse(definitionResolverCalled);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_PreCanceledToken_ThrowsOperationCanceledException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			using var cancellationTokenSource = new CancellationTokenSource();
			cancellationTokenSource.Cancel();

			await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
				editor.TextArea.GoToDefinitionByHoverAsync(
					DocumentId,
					definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
					hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(CreateHoverInfo()),
					offset: 0,
					cancellationToken: cancellationTokenSource.Token
				)
			);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_NullResolvers_ThrowArgumentNullException()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
				editor.TextArea.GoToDefinitionByHoverAsync(
					DocumentId,
					definitionResolverAsync: null!,
					hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(CreateHoverInfo()),
					offset: 0,
					cancellationToken: TestContext.CancellationToken
				)
			);

			await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>
				editor.TextArea.GoToDefinitionByHoverAsync(
					DocumentId,
					definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(1)),
					hoverResolverAsync: null!,
					offset: 0,
					cancellationToken: TestContext.CancellationToken
				)
			);
		}
	}

	[TestMethod]
	public void GoToDefinitionByHoverAsync_PoolThreadHoverContinuation_NavigatesOnTheEditorThread()
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
			Task<bool> navigation = editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(3, 8)),

				hoverResolverAsync: async (_, _) =>
				{
					hoverResolved.TrySetResult();
					await hoverGate.Task.ConfigureAwait(false);
					return CreateHoverInfo();
				},

				offset: 0,
				cancellationToken: TestContext.CancellationToken
			);

			// Wait on the resolver's start signal instead of a fixed pump window, so the gate is released only
			// once the hover resolution is genuinely in flight.
			DispatcherTestHelpers.PumpUntil(() => hoverResolved.Task.IsCompleted);
			hoverGate.SetResult();
			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsTrue(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(dispatcherThreadId, caretThreadId, "Navigation must be applied on the editor thread.");
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_CrossFileLocationWithoutCallback_ReturnsFalse()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();

		using (hostWindow)
		{
			Assert.IsFalse(
				await GoToDefinitionByHoverAsync(
					textArea: editor.TextArea,
					definitionProvider: new StubDefinitionProvider(LocationAt(1, documentId: "other.txt")),
					hoverProvider: new StubHoverProvider(CreateHoverInfo()),
					offset: 0
				)
			);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_CrossFileLocation_InvokesTheCrossFileCallback()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		DefinitionLocation? forwarded = null;

		using (hostWindow)
		{
			DefinitionLocation location = LocationAt(1, documentId: "other.txt");

			// Placing the caret away from the document start makes the "navigation was not applied to this
			// editor" assertion meaningful.
			editor.CaretOffset = 3;

			bool navigated = await editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(location),
				hoverResolverAsync: (_, _) => Task.FromResult<HoverInformation?>(CreateHoverInfo()),
				offset: 0,

				tryNavigateCrossFileDefinitionAsync: (candidate, _) =>
				{
					forwarded = candidate;
					return Task.FromResult(true);
				},

				cancellationToken: TestContext.CancellationToken
			);

			Assert.IsTrue(navigated);
			Assert.AreSame(location, forwarded);
			Assert.AreEqual(3, editor.CaretOffset);
		}
	}

	[TestMethod]
	public async Task GoToDefinitionByHoverAsync_DocumentEditedWhileHoverResolverRuns_DoesNotApplyTheStaleLocation()
	{
		(TextEditor editor, HostWindow hostWindow) = CreateHostedEditor();
		var hoverGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using (hostWindow)
		{
			Task<bool> navigation = editor.TextArea.GoToDefinitionByHoverAsync(
				DocumentId,
				definitionResolverAsync: (_, _) => Task.FromResult<DefinitionLocation?>(LocationAt(2, 8)),

				hoverResolverAsync: async (_, _) =>
				{
					await hoverGate.Task.ConfigureAwait(true);
					return CreateHoverInfo();
				},

				offset: 0,
				cancellationToken: TestContext.CancellationToken
			);

			// The document changes while the hover resolver is suspended; the definition that was resolved
			// from the previous snapshot must not be applied to the edited text.
			editor.Document.Insert(editor.Document.TextLength, " edited");
			hoverGate.SetResult();

			DispatcherTestHelpers.PumpUntil(() => navigation.IsCompleted);

			Assert.IsFalse(navigation.GetAwaiter().GetResult());
			Assert.AreEqual(0, editor.CaretOffset);
		}
	}
}
