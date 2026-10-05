using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the signature help controller's request coordination, request scheduling, disposal, and overload navigation.</summary>
/// <remarks>
/// The tests share <see cref="SignatureHelpTestHost"/>, which records what the controller reported to the host and owns the pumped dispatcher
/// the controller runs on. A test that awaits a pending provider pumps the dispatcher instead, because the host installs its synchronization context.
/// </remarks>
[TestClass]
public sealed partial class SignatureHelpControllerTests
{
	internal static SignatureHelpInformation CreateDefaultSignatureHelp() =>
		new([new SignatureInformation("sample(alpha)", "Sample documentation.")]);

	[TestMethod]
	public async Task Dispose_WhenSignatureHelpIsVisible_ClearsStateAndInvokesDismiss()
	{
		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);

		controller.Dispose();

		// Disposal clears the tracked state and asks the host to dismiss its popup.
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestScheduled);
		Assert.AreEqual(1, host.DismissCount);

		// Disposal is idempotent and does not dismiss twice.
		controller.Dispose();

		Assert.AreEqual(1, host.DismissCount);
	}

	[TestMethod]
	public void Dismiss_WithNothingVisible_StillInvokesTheDismissCallback()
	{
		using var host = new SignatureHelpTestHost();
		using SignatureHelpController controller = host.CreateController();

		// The contract requires hosts to make the dismiss callback idempotent: it is invoked even when the
		// controller tracks nothing visible.
		controller.Dismiss();

		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void Dismiss_WithThrowingCallback_IsContained()
	{
		using var host = new SignatureHelpTestHost { DismissCallback = () => throw new InvalidOperationException("Dismiss failed.") };

		using SignatureHelpController controller = host.CreateController();

		// A throwing dismiss callback is contained; the controller still tracks the hidden state.
		controller.Dismiss();

		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void Constructor_NullCaretReaderProviderOrDispatcher_ThrowsArgumentNullException()
	{
		using var host = new SignatureHelpTestHost();

		SignatureHelpControllerHooks hooks = CreateHooks();
		FakePumpedDispatcher dispatcher = host.Dispatcher;

		Assert.ThrowsExactly<ArgumentNullException>(() => new SignatureHelpController(getCaretOffset: null!, provider: host, hooks, dispatcher));

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new SignatureHelpController(getCaretOffset: static () => 0, provider: null!, hooks, dispatcher)
		);

		Assert.ThrowsExactly<ArgumentNullException>(() =>
			new SignatureHelpController(getCaretOffset: static () => 0, provider: host, hooks, dispatcher: null!)
		);
	}

	[TestMethod]
	public void Constructor_EachNullRequiredHook_ThrowsArgumentNullException()
	{
		using var host = new SignatureHelpTestHost();

		FakePumpedDispatcher dispatcher = host.Dispatcher;

		// The hooks are a value type, so a host that smuggles in a null delegate (or a default value) still gets a guarded failure naming the
		// offending hook.
		ArgumentNullException Reject(SignatureHelpControllerHooks hooks)
		{
			return Assert.ThrowsExactly<ArgumentNullException>(() =>
				new SignatureHelpController(getCaretOffset: static () => 0, provider: host, hooks, dispatcher)
			);
		}

		ArgumentNullException requestNull = Reject(
			new SignatureHelpControllerHooks(BuildRequest: null!, ShowSignatureHelp: static (_, _) => { }, DismissSignatureHelp: static () => { })
		);

		Assert.Contains("BuildRequest", requestNull.ParamName);

		ArgumentNullException showNull = Reject(
			new SignatureHelpControllerHooks(BuildRequest: static (_, _) => null, ShowSignatureHelp: null!, DismissSignatureHelp: static () => { })
		);

		Assert.Contains("ShowSignatureHelp", showNull.ParamName);

		ArgumentNullException dismissNull = Reject(
			new SignatureHelpControllerHooks(
				BuildRequest: static (_, _) => null,
				ShowSignatureHelp: static (_, _) => { },
				DismissSignatureHelp: null!
			)
		);

		Assert.Contains("DismissSignatureHelp", dismissNull.ParamName);

		string? defaultParamName = Reject(default).ParamName;

		Assert.IsNotNull(defaultParamName);
		Assert.Contains("BuildRequest", defaultParamName);
	}

	[TestMethod]
	public void Constructor_OffTheDispatcherThread_ThrowsInvalidOperationException()
	{
		using var host = new SignatureHelpTestHost();

		// The controller publishes presentation state on the dispatcher's thread, so creating it elsewhere is a caller error.
		Exception? failure = CaptureOffThreadFailure(() => _ = host.CreateController());

		Assert.IsInstanceOfType<InvalidOperationException>(failure);
	}

	[TestMethod]
	public async Task RequestAsync_BuildRequestReturnsNull_DismissesWithoutAskingTheProvider()
	{
		using var host = new SignatureHelpTestHost
		{
			BuildRequest = static (_, _) => null,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),
		};

		using SignatureHelpController controller = host.CreateController();

		bool published = await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// A host that cannot serve the document vetoes the request, which presents nothing like a provider that answered null.
		Assert.IsFalse(published);
		Assert.AreSequenceEqual([], host.RequestOffsets);
		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public async Task RequestAsync_BuildRequest_ReceivesTheOffsetAndTheRetriggerContext()
	{
		var contexts = new List<SignatureHelpContext>();

		using var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(CreateDefaultSignatureHelp()),

			BuildRequest = (offset, context) =>
			{
				contexts.Add(context);

				return new SignatureHelpRequest(
					documentId: SignatureHelpTestHost.DocumentId,
					documentText: SignatureHelpTestHost.DocumentText,
					caretOffset: offset,
					context
				);
			},
		};

		using SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		await controller.RequestAsync(6, SignatureHelpTriggerKind.ContentChange, cancellationToken: TestContext.CancellationToken);

		// The controller owns the retrigger state, so the second request is marked as a retrigger that carries the visible payload.
		Assert.HasCount(2, contexts);
		Assert.IsFalse(contexts[0].IsRetrigger);
		Assert.IsTrue(contexts[1].IsRetrigger);
		Assert.AreSequenceEqual([5, 6], host.RequestOffsets);
	}

	private static SignatureHelpControllerHooks CreateHooks()
	{
		return new SignatureHelpControllerHooks(
			BuildRequest: static (_, _) => null,
			ShowSignatureHelp: static (_, _) => { },
			DismissSignatureHelp: static () => { }
		);
	}
}
