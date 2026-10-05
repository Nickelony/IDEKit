using Nickelony.IDEKit.LanguageFeatures.Completion;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Tests;

/// <summary>Verifies the derived conveniences of the completion presentation-state record.</summary>
[TestClass]
public sealed class CompletionPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrScheduled()
	{
		CompletionPresentationState state = CompletionPresentationState.Empty;

		Assert.IsFalse(state.IsListVisible);
		Assert.IsFalse(state.IsRequestScheduled);
		Assert.IsFalse(state.IsTooltipVisible);
		Assert.IsNull(state.TooltipContent);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	[DataRow(true, false, false)]
	[DataRow(false, true, false)]
	[DataRow(false, false, true)]
	public void IsPresentationVisibleOrRequestScheduled_CoversListTooltipAndScheduledRequest(
		bool isListVisible,
		bool isRequestScheduled,
		bool isTooltipVisible
	)
	{
		var state = new CompletionPresentationState(isListVisible, isRequestScheduled, isTooltipVisible, TooltipContent: null);

		// Each of the three presentation triggers alone reports the presentation visible or a request scheduled.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	public void IsPresentationVisibleOrRequestScheduled_WithOnlyTooltipContent_IsFalse()
	{
		// The tooltip content alone is not part of the union: it counts only while the tooltip is
		// flagged as visible.
		var state = new CompletionPresentationState(
			IsListVisible: false,
			IsRequestScheduled: false,
			IsTooltipVisible: false,
			TooltipContent: new CompletionTooltipContent("docs")
		);

		Assert.IsNotNull(state.TooltipContent);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}
}
