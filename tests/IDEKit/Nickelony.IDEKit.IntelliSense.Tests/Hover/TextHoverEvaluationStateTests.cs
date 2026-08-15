using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Diagnostics;
using Nickelony.IDEKit.IntelliSense.Hover;

namespace Nickelony.IDEKit.IntelliSense.Tests.Hover;

/// <summary>
/// Verifies the storage, defaults, and value equality of the hover evaluation-state record.
/// </summary>
[TestClass]
public sealed class TextHoverEvaluationStateTests
{
	[TestMethod]
	public void Initializer_StoresEveryComponent()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Warning, "message", 2, 4);
		var state = new TextHoverEvaluationState
		{
			ShouldRequestHover = true,
			RequestOffset = 3,
			CanShowHoverContent = true,
			CanShowDiagnosticFallback = false,
			DiagnosticInfo = diagnostic
		};

		Assert.IsTrue(state.ShouldRequestHover);
		Assert.AreEqual(3, state.RequestOffset);
		Assert.IsTrue(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.AreSame(diagnostic, state.DiagnosticInfo);
	}

	[TestMethod]
	public void Default_UsesSafeDefaults()
	{
		TextHoverEvaluationState state = default;

		Assert.IsFalse(state.ShouldRequestHover);
		Assert.AreEqual(0, state.RequestOffset);
		Assert.IsFalse(state.CanShowHoverContent);
		Assert.IsFalse(state.CanShowDiagnosticFallback);
		Assert.IsNull(state.DiagnosticInfo);
	}

	[TestMethod]
	public void Equality_ComparesEveryComponent()
	{
		var diagnostic = new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 1, 2);
		var state = CreateState(true, 3, true, true, diagnostic);

		// A distinct but equal diagnostic must compare equal, so the record compares the diagnostic by
		// value rather than by the identity of a reused instance.
		Assert.AreEqual(state, CreateState(true, 3, true, true, new TextDiagnostic(TextDiagnosticSeverity.Error, "boom", 1, 2)));
		Assert.AreNotEqual(state, CreateState(false, 3, true, true, diagnostic));
		Assert.AreNotEqual(state, CreateState(true, 4, true, true, diagnostic));
		Assert.AreNotEqual(state, CreateState(true, 3, false, true, diagnostic));
		Assert.AreNotEqual(state, CreateState(true, 3, true, false, diagnostic));
		Assert.AreNotEqual(state, CreateState(true, 3, true, true, new TextDiagnostic(TextDiagnosticSeverity.Error, "other", 1, 2)));
		Assert.AreNotEqual(state, CreateState(true, 3, true, true, null));
	}

	private static TextHoverEvaluationState CreateState(
		bool shouldRequestHover,
		int requestOffset,
		bool canShowHoverContent,
		bool canShowDiagnosticFallback,
		TextDiagnostic? diagnosticInfo)
		=> new()
		{
			ShouldRequestHover = shouldRequestHover,
			RequestOffset = requestOffset,
			CanShowHoverContent = canShowHoverContent,
			CanShowDiagnosticFallback = canShowDiagnosticFallback,
			DiagnosticInfo = diagnosticInfo
		};
}
