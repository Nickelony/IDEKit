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
/// Verifies the derived conveniences of the signature help presentation-state record.
/// </summary>
[TestClass]
public sealed class TextSignatureHelpPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrPending()
	{
		TextSignatureHelpPresentationState state = TextSignatureHelpPresentationState.Empty;

		Assert.IsNull(state.SignatureHelp);
		Assert.IsFalse(state.IsVisible);
		Assert.IsFalse(state.IsRequestInFlight);
		Assert.IsFalse(state.IsRefreshPending);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	[DataRow(true, false, false)]
	[DataRow(false, true, false)]
	[DataRow(false, false, true)]
	public void IsPresentationVisibleOrRequestPending_CoversVisibilityRequestAndRefresh(
		bool isVisible,
		bool isRequestInFlight,
		bool isRefreshPending)
	{
		var state = new TextSignatureHelpPresentationState(null, isVisible, isRequestInFlight, isRefreshPending);

		// Unlike the completion record, the signature help union includes an in-flight request and a
		// pending refresh.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestPending);
	}

	[TestMethod]
	public void IsPresentationVisibleOrRequestPending_WithOnlySignatureHelp_IsFalse()
	{
		// The payload alone is not part of the union: it counts only while the popup is flagged as
		// visible, a request is in flight, or a refresh is pending.
		var signatureHelp = new TextSignatureHelp([new TextSignatureInformation("print()")]);
		var state = new TextSignatureHelpPresentationState(signatureHelp, false, false, false);
		Assert.IsNotNull(state.SignatureHelp);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestPending);
	}
}
