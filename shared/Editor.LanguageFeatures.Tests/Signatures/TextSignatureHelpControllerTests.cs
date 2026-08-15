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

/// <summary>
/// Verifies the signature help controller's request coordination, refresh scheduling, disposal, and
/// overload navigation. The tests share <see cref="SignatureHelpTestHost"/>, which records what the
/// controller reported to the host.
/// </summary>
[STATestClass]
public sealed partial class TextSignatureHelpControllerTests
{
	internal static TextSignatureHelp CreateDefaultSignatureHelp()
	{
		return new TextSignatureHelp([new TextSignatureInformation("sample(alpha)", "Sample documentation.")]);
	}

	[TestMethod]
	public async Task Dispose_WhenSignatureHelpIsVisible_ClearsStateAndInvokesDismiss()
	{
		var host = new SignatureHelpTestHost
		{
			RequestSignatureHelpAsync = (_, _, _) => Task.FromResult<TextSignatureHelp?>(CreateDefaultSignatureHelp())
		};

		var controller = host.CreateController();

		await controller.RequestAsync(5);
		Assert.IsTrue(controller.CurrentPresentation.IsVisible);

		controller.Dispose();

		// Disposal clears the tracked state and asks the host to dismiss its popup.
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
		Assert.IsFalse(controller.CurrentPresentation.IsPresentationVisibleOrRequestPending);
		Assert.AreEqual(1, host.DismissCount);

		// Disposal is idempotent and does not dismiss twice.
		controller.Dispose();

		Assert.AreEqual(1, host.DismissCount);
	}

	[TestMethod]
	public void Dismiss_WithNothingVisible_StillInvokesTheDismissCallback()
	{
		var host = new SignatureHelpTestHost();
		using var controller = host.CreateController();

		// The contract requires hosts to make the dismiss callback idempotent: it is invoked even when the
		// controller tracks nothing visible.
		controller.Dismiss();

		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void Dismiss_WithThrowingCallback_IsContained()
	{
		var host = new SignatureHelpTestHost
		{
			DismissCallback = () => throw new InvalidOperationException("Dismiss failed.")
		};

		using var controller = host.CreateController();

		// A throwing dismiss callback is contained; the controller still tracks the hidden state.
		controller.Dismiss();

		Assert.AreEqual(1, host.DismissCount);
		Assert.IsFalse(controller.CurrentPresentation.IsVisible);
	}

	[TestMethod]
	public void Constructor_NullHooks_ThrowsArgumentNullException()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelpController(null!));
	}

	[TestMethod]
	public void Constructor_NullRequiredHook_ThrowsArgumentNullException()
	{
		var hooks = new TextSignatureHelpControllerHooks
		{
			GetCurrentCaretOffset = () => 0,
			RequestSignatureHelpAsync = null!,
			ShowSignatureHelp = _ => { },
			DismissSignatureHelp = () => { }
		};

		// The required hooks are enforced at compile time by the hook type; a host that smuggles in null
		// still gets a guarded failure naming the offending hook.
		var exception = Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelpController(hooks));

		StringAssert.Contains(exception.ParamName, "RequestSignatureHelpAsync");
	}

	[TestMethod]
	public void Constructor_EachOtherNullRequiredHook_ThrowsArgumentNullException()
	{
		// The provider hook is pinned by the sibling test; the remaining required hooks are guarded
		// individually as well.
		ArgumentNullException caretNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelpController(new TextSignatureHelpControllerHooks
		{
			GetCurrentCaretOffset = null!,
			RequestSignatureHelpAsync = static (_, _, _) => Task.FromResult<TextSignatureHelp?>(null),
			ShowSignatureHelp = static _ => { },
			DismissSignatureHelp = static () => { }
		}));
		StringAssert.Contains(caretNull.ParamName, "GetCurrentCaretOffset");

		ArgumentNullException showNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelpController(new TextSignatureHelpControllerHooks
		{
			GetCurrentCaretOffset = static () => 0,
			RequestSignatureHelpAsync = static (_, _, _) => Task.FromResult<TextSignatureHelp?>(null),
			ShowSignatureHelp = null!,
			DismissSignatureHelp = static () => { }
		}));
		StringAssert.Contains(showNull.ParamName, "ShowSignatureHelp");

		ArgumentNullException dismissNull = Assert.ThrowsExactly<ArgumentNullException>(() => new TextSignatureHelpController(new TextSignatureHelpControllerHooks
		{
			GetCurrentCaretOffset = static () => 0,
			RequestSignatureHelpAsync = static (_, _, _) => Task.FromResult<TextSignatureHelp?>(null),
			ShowSignatureHelp = static _ => { },
			DismissSignatureHelp = null!
		}));
		StringAssert.Contains(dismissNull.ParamName, "DismissSignatureHelp");
	}
}
