using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Hover;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;
using Nickelony.IDEKit.LanguageFeatures.Hover;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the storage, defaults, and value equality of the hover evaluation record.</summary>
[TestClass]
public sealed class HoverDecisionTests
{
	[TestMethod]
	public void Initializer_StoresEveryComponent()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Warning, message: "message", range: TextRange.FromBounds(2, 4));

		var state = new HoverDecision
		{
			ShouldRequestHover = true,
			RequestOffset = 3,
			CanShowHoverContent = true,
			CanShowDiagnosticFallback = false,
			Diagnostic = diagnostic,
		};

		Assert.IsTrue(state.ShouldRequestHover);
		Assert.AreEqual(3, state.RequestOffset);
		Assert.IsTrue(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.AreSame(diagnostic, state.Diagnostic);
	}

	[TestMethod]
	public void Default_UsesSafeDefaults()
	{
		HoverDecision state = default;

		Assert.IsFalse(state.ShouldRequestHover);
		Assert.AreEqual(0, state.RequestOffset);
		Assert.IsFalse(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.IsNull(state.Diagnostic);
	}

	[TestMethod]
	public void Equality_ComparesEveryComponent()
	{
		var diagnostic = new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(1, 2));

		HoverDecision state = CreateState(
			shouldRequestHover: true,
			requestOffset: 3,
			canShowHoverContent: true,
			canShowDiagnosticFallback: true,
			diagnosticInfo: diagnostic
		);

		// A distinct but equal diagnostic must compare equal, so the record compares the diagnostic by
		// value rather than by the identity of a reused instance.
		Assert.AreEqual(
			state,

			CreateState(
				shouldRequestHover: true,
				requestOffset: 3,
				canShowHoverContent: true,
				canShowDiagnosticFallback: true,
				diagnosticInfo: new Diagnostic(severity: DiagnosticSeverity.Error, message: "boom", range: TextRange.FromBounds(1, 2))
			)
		);

		Assert.AreNotEqual(
			state,

			CreateState(
				shouldRequestHover: false,
				requestOffset: 3,
				canShowHoverContent: true,
				canShowDiagnosticFallback: true,
				diagnosticInfo: diagnostic
			)
		);

		Assert.AreNotEqual(
			state,

			CreateState(
				shouldRequestHover: true,
				requestOffset: 4,
				canShowHoverContent: true,
				canShowDiagnosticFallback: true,
				diagnosticInfo: diagnostic
			)
		);

		Assert.AreNotEqual(
			state,

			CreateState(
				shouldRequestHover: true,
				requestOffset: 3,
				canShowHoverContent: false,
				canShowDiagnosticFallback: true,
				diagnosticInfo: diagnostic
			)
		);

		Assert.AreNotEqual(
			state,

			CreateState(
				shouldRequestHover: true,
				requestOffset: 3,
				canShowHoverContent: true,
				canShowDiagnosticFallback: false,
				diagnosticInfo: diagnostic
			)
		);

		Assert.AreNotEqual(
			state,

			CreateState(
				shouldRequestHover: true,
				requestOffset: 3,
				canShowHoverContent: true,
				canShowDiagnosticFallback: true,
				diagnosticInfo: new Diagnostic(severity: DiagnosticSeverity.Error, message: "other", range: TextRange.FromBounds(1, 2))
			)
		);

		Assert.AreNotEqual(
			state,
			CreateState(shouldRequestHover: true, requestOffset: 3, canShowHoverContent: true, canShowDiagnosticFallback: true, diagnosticInfo: null)
		);
	}

	private static HoverDecision CreateState(
		bool shouldRequestHover,
		int requestOffset,
		bool canShowHoverContent,
		bool canShowDiagnosticFallback,
		Diagnostic? diagnosticInfo
	)
	{
		return new()
		{
			ShouldRequestHover = shouldRequestHover,
			RequestOffset = requestOffset,
			CanShowHoverContent = canShowHoverContent,
			CanShowDiagnosticFallback = canShowDiagnosticFallback,
			Diagnostic = diagnosticInfo,
		};
	}
}
