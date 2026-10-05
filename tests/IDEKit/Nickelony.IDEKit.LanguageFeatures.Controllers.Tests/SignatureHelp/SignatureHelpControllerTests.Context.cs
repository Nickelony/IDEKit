using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

public sealed partial class SignatureHelpControllerTests
{
	public required TestContext TestContext { get; set; }

	[TestMethod]
	public async Task RequestAsync_PassesTriggerContextToProvider()
	{
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null) };

		using SignatureHelpController controller = host.CreateController();
		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);

		// An explicit invocation is not a retrigger and carries no active payload.
		Assert.HasCount(1, host.Contexts);
		Assert.AreEqual(SignatureHelpTriggerKind.Invoked, host.Contexts[0].TriggerKind);
		Assert.IsNull(host.Contexts[0].TriggerCharacter);
		Assert.IsFalse(host.Contexts[0].IsRetrigger);
		Assert.IsNull(host.Contexts[0].ActiveSignatureHelp);
		Assert.AreSequenceEqual([5], host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_TriggerCharacter_IsReportedInContext()
	{
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null) };

		using SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(
			offset: 5,
			triggerKind: SignatureHelpTriggerKind.TriggerCharacter,
			triggerCharacter: "(",
			cancellationToken: TestContext.CancellationToken
		);

		Assert.HasCount(1, host.Contexts);
		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, host.Contexts[0].TriggerKind);
		Assert.AreEqual("(", host.Contexts[0].TriggerCharacter);
	}

	[TestMethod]
	public async Task RequestAsync_WhileHelpVisible_ReportsRetriggerContext()
	{
		SignatureHelpInformation signature = CreateDefaultSignatureHelp();
		using var host = new SignatureHelpTestHost { RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature) };

		SignatureHelpController controller = host.CreateController();

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		await controller.RequestAsync(9, cancellationToken: TestContext.CancellationToken);

		// The second request is a retrigger and carries the payload that was visible when it started.
		Assert.HasCount(2, host.Contexts);
		Assert.IsFalse(host.Contexts[0].IsRetrigger);
		Assert.IsTrue(host.Contexts[1].IsRetrigger);
		Assert.AreSame(signature, host.Contexts[1].ActiveSignatureHelp);

		controller.Dispose();
	}

	[TestMethod]
	public async Task ScheduleRequest_WhileHelpVisible_ReportsContentChangeRetrigger()
	{
		SignatureHelpInformation signature = CreateDefaultSignatureHelp();

		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(signature),
		};

		SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		await controller.RequestAsync(5, cancellationToken: TestContext.CancellationToken);
		controller.ScheduleRequest();

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		// The scheduled request is reported as a content change retrigger with the visible payload attached.
		Assert.AreEqual(SignatureHelpTriggerKind.ContentChange, host.Contexts[1].TriggerKind);
		Assert.IsTrue(host.Contexts[1].IsRetrigger);
		Assert.AreSame(signature, host.Contexts[1].ActiveSignatureHelp);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRequest_WithTriggerCharacter_ReportsItInContext()
	{
		using var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<SignatureHelpInformation?>(null),
		};

		using SignatureHelpController controller = host.CreateController(
			SignatureHelpControllerOptions.Default with
			{
				RequestDebounceDelay = TimeSpan.FromMilliseconds(1.0),
			}
		);

		controller.ScheduleRequest(SignatureHelpTriggerKind.TriggerCharacter, "(");

		host.Dispatcher.Advance(TimeSpan.FromMilliseconds(1.0));

		// The debounced request runs at the current caret offset and reports the supplied trigger data.
		Assert.AreEqual(SignatureHelpTriggerKind.TriggerCharacter, host.Contexts[0].TriggerKind);
		Assert.AreEqual("(", host.Contexts[0].TriggerCharacter);
		Assert.AreSequenceEqual([5], host.RequestOffsets);
	}
}
