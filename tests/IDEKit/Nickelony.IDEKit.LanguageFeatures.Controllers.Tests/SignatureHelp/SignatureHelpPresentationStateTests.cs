using Nickelony.IDEKit.LanguageFeatures.Controllers.SignatureHelp;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the derived conveniences of the signature help presentation-state record.</summary>
[TestClass]
public sealed class SignatureHelpPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrScheduled()
	{
		SignatureHelpPresentationState state = SignatureHelpPresentationState.Empty;

		Assert.IsNull(state.SignatureHelp);
		Assert.IsFalse(state.IsVisible);
		Assert.IsFalse(state.IsRequestInFlight);
		Assert.IsFalse(state.IsRequestScheduled);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	[DataRow(true, false, false)]
	[DataRow(false, true, false)]
	[DataRow(false, false, true)]
	public void IsPresentationVisibleOrRequestScheduled_CoversVisibilityRequestAndSchedule(
		bool isVisible,
		bool isRequestInFlight,
		bool isRequestScheduled
	)
	{
		var state = new SignatureHelpPresentationState(SignatureHelp: null, isVisible, isRequestInFlight, isRequestScheduled);

		// Unlike the completion record, the signature help union includes an in-flight request and a
		// scheduled request.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	public void IsPresentationVisibleOrRequestScheduled_WithOnlySignatureHelp_IsFalse()
	{
		// The payload alone is not part of the union: it counts only while the popup is flagged as
		// visible, a request is in flight, or a request is scheduled.
		var signatureHelp = new SignatureHelpInformation([new SignatureInformation("print()")]);

		var state = new SignatureHelpPresentationState(signatureHelp, IsVisible: false, IsRequestInFlight: false, IsRequestScheduled: false);

		Assert.IsNotNull(state.SignatureHelp);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}
}
