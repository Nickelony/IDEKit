using Microsoft.Extensions.Logging;
using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>
/// Builds a signature help controller over mutable hooks and records every host callback and provider call the controller makes, so the tests
/// do not repeat the hook set. The host is the provider the controller asks, and it owns the dispatcher the controller runs on.
/// </summary>
/// <remarks>
/// The caret is a delegate the test sets, so a test states the offset it needs without an editor. The host disposes its dispatcher,
/// which restores the thread's synchronization context.
/// </remarks>
internal sealed class SignatureHelpTestHost : ISignatureHelpProvider, IDisposable
{
	/// <summary>The document identifier the requests the host builds carry.</summary>
	public const string DocumentId = "signature-test.lua";

	/// <summary>The document text of the request; its length bounds the offsets a request can address.</summary>
	public static readonly string DocumentText = new('x', 100);

	/// <param name="installSynchronizationContext">
	/// Whether the dispatcher is the synchronization context of the test thread, as the dispatcher of a running editor is. A test that needs
	/// a continuation to resume on the thread pool passes <see langword="false"/>.
	/// </param>
	public SignatureHelpTestHost(bool installSynchronizationContext = true) => Dispatcher = new FakePumpedDispatcher(installSynchronizationContext);

	/// <summary>The dispatcher the controller runs on; the test pumps it.</summary>
	public FakePumpedDispatcher Dispatcher { get; }

	/// <summary>The payloads the controller asked the host to show, in order.</summary>
	public List<SignatureHelpInformation> ShownSignatures { get; } = [];

	/// <summary>The offsets the controller showed the payloads at, in order, one per entry of <see cref="ShownSignatures"/>.</summary>
	public List<int> ShownOffsets { get; } = [];

	/// <summary>The request contexts the controller passed to the provider, in order.</summary>
	public List<SignatureHelpContext> Contexts { get; } = [];

	/// <summary>The offsets the controller asked the host to build a request for, in order, including those the host then vetoed.</summary>
	public List<int> BuildOffsets { get; } = [];

	/// <summary>The offsets the controller requested, in order.</summary>
	public List<int> RequestOffsets { get; } = [];

	/// <summary>The cancellation tokens the controller passed to the provider, in order.</summary>
	public List<CancellationToken> RequestTokens { get; } = [];

	/// <summary>The number of times the controller invoked the dismiss callback.</summary>
	public int DismissCount { get; private set; }

	/// <summary>Holds the caret-offset read, which defaults to offset <c>0</c>; <see langword="null"/> or a negative value means no caret.</summary>
	public Func<int?> GetCurrentCaretOffset { get; set; } = static () => 0;

	/// <summary>Holds the request builder, which defaults to a request for the host document that carries the context.</summary>
	public Func<int, SignatureHelpContext, SignatureHelpRequest?> BuildRequest { get; set; } =
		static (offset, context) => new SignatureHelpRequest(DocumentId, DocumentText, caretOffset: offset, context);

	/// <summary>Holds the provider behavior, which defaults to a <see langword="null"/> result.</summary>
	public Func<int, SignatureHelpContext, CancellationToken, Task<SignatureHelpInformation?>> RequestSignatureHelpAsync { get; set; } =
		static (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null);

	/// <summary>Holds an optional callback invoked after the show callback recorded the payload.</summary>
	public Action<SignatureHelpInformation>? ShowCallback { get; set; }

	/// <summary>Holds an optional callback invoked after the dismiss invocation was recorded.</summary>
	public Action? DismissCallback { get; set; }

	/// <inheritdoc/>
	public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
	{
		SignatureHelpContext context = request.Context ?? new SignatureHelpContext(SignatureHelpTriggerKind.Invoked);

		RequestOffsets.Add(request.CaretOffset);
		Contexts.Add(context);
		RequestTokens.Add(cancellationToken);

		return RequestSignatureHelpAsync(request.CaretOffset, context, cancellationToken);
	}

	/// <summary>Creates a controller wired to this host's caret, hooks, provider and dispatcher.</summary>
	/// <param name="options">The controller options, or <see langword="null"/> for the defaults.</param>
	/// <param name="logger">The optional logger for controller failures.</param>
	/// <returns>The created controller.</returns>
	public SignatureHelpController CreateController(SignatureHelpControllerOptions? options = null, ILogger? logger = null)
	{
		return new SignatureHelpController(
			getCaretOffset: () => GetCurrentCaretOffset(),
			provider: this,
			hooks: CreateHooks(),
			Dispatcher,
			options,
			logger
		);
	}

	/// <summary>Creates the hook set that records into this host.</summary>
	/// <returns>The hooks.</returns>
	public SignatureHelpControllerHooks CreateHooks()
	{
		return new SignatureHelpControllerHooks(
			BuildRequest: (offset, context) =>
			{
				BuildOffsets.Add(offset);
				return BuildRequest(offset, context);
			},

			ShowSignatureHelp: (signatureHelp, offset) =>
			{
				ShownSignatures.Add(signatureHelp);
				ShownOffsets.Add(offset);

				ShowCallback?.Invoke(signatureHelp);
			},

			DismissSignatureHelp: () =>
			{
				DismissCount++;
				DismissCallback?.Invoke();
			}
		);
	}

	/// <summary>Disposes the dispatcher, which restores the synchronization context it installed.</summary>
	public void Dispose() => Dispatcher.Dispose();
}
