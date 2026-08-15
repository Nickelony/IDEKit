#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Signatures;
using Nickelony.IDEKit.IntelliSense.Signatures;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

public sealed partial class TextSignatureHelpControllerTests
{
	[TestMethod]
	public async Task RequestAsync_PassesTriggerContextToProvider()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5);

		// An explicit invocation is not a retrigger and carries no active payload.
		Assert.AreEqual(1, host.Contexts.Count);
		Assert.AreEqual(TextSignatureHelpTriggerKind.Invoked, host.Contexts[0].TriggerKind);
		Assert.IsNull(host.Contexts[0].TriggerCharacter);
		Assert.IsFalse(host.Contexts[0].IsRetrigger);
		Assert.IsNull(host.Contexts[0].ActiveSignatureHelp);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}

	[TestMethod]
	public async Task RequestAsync_TriggerCharacter_IsReportedInContext()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		using var controller = host.CreateController();
		await controller.RequestAsync(5, TextSignatureHelpTriggerKind.TriggerCharacter, "(");

		Assert.AreEqual(1, host.Contexts.Count);
		Assert.AreEqual(TextSignatureHelpTriggerKind.TriggerCharacter, host.Contexts[0].TriggerKind);
		Assert.AreEqual("(", host.Contexts[0].TriggerCharacter);
	}

	[TestMethod]
	public async Task RequestAsync_WhileHelpVisible_ReportsRetriggerContext()
	{
		var signature = CreateDefaultSignatureHelp();
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		var controller = host.CreateController();

		await controller.RequestAsync(5);
		await controller.RequestAsync(9);

		// The second request is a retrigger and carries the payload that was visible when it started.
		Assert.AreEqual(2, host.Contexts.Count);
		Assert.IsFalse(host.Contexts[0].IsRetrigger);
		Assert.IsTrue(host.Contexts[1].IsRetrigger);
		Assert.AreSame(signature, host.Contexts[1].ActiveSignatureHelp);

		controller.Dispose();
	}

	[TestMethod]
	public async Task ScheduleRefresh_WhileHelpVisible_ReportsContentChangeRetrigger()
	{
		var signature = CreateDefaultSignatureHelp();
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(signature)
		};

		var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		await controller.RequestAsync(5);
		controller.ScheduleRefresh();

		DispatcherTestUtils.PumpUntil(() => host.Contexts.Count >= 2);

		// The refresh is reported as a content change retrigger with the visible payload attached.
		Assert.AreEqual(TextSignatureHelpTriggerKind.ContentChange, host.Contexts[1].TriggerKind);
		Assert.IsTrue(host.Contexts[1].IsRetrigger);
		Assert.AreSame(signature, host.Contexts[1].ActiveSignatureHelp);

		controller.Dispose();
	}

	[TestMethod]
	public void ScheduleRefresh_WithTriggerCharacter_ReportsItInContext()
	{
		var host = new SignatureHelpTestHost
		{
			GetCurrentCaretOffset = () => 5,
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(null)
		};

		using var controller = host.CreateController(
			TextSignatureHelpControllerOptions.Default with { RefreshDebounceDelay = TimeSpan.FromMilliseconds(1.0) });

		controller.ScheduleRefresh(TextSignatureHelpTriggerKind.TriggerCharacter, "(");

		DispatcherTestUtils.PumpUntil(() => host.Contexts.Count >= 1);

		// The debounced refresh runs at the current caret offset and reports the supplied trigger data.
		Assert.AreEqual(TextSignatureHelpTriggerKind.TriggerCharacter, host.Contexts[0].TriggerKind);
		Assert.AreEqual("(", host.Contexts[0].TriggerCharacter);
		CollectionAssert.AreEqual(new[] { 5 }, host.RequestOffsets);
	}
}
