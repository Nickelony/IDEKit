#if AVALONIAEDIT
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using STATestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestClassAttribute = Nickelony.IDEKit.Testing.AvaloniaTestClassAttribute;
using TestHost = Nickelony.IDEKit.Testing.AvaloniaTestHost;
namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;
#else
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using TestHost = Nickelony.IDEKit.Testing.WPFTestHost;
namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;
#endif

/// <summary>
/// Verifies the derived conveniences of the completion presentation-state record.
/// </summary>
[TestClass]
public sealed class TextCompletionPresentationStateTests
{
	[TestMethod]
	public void Empty_ReportsNothingVisibleOrScheduled()
	{
		TextCompletionPresentationState state = TextCompletionPresentationState.Empty;

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
		bool isTooltipVisible)
	{
		var state = new TextCompletionPresentationState(isListVisible, isRequestScheduled, isTooltipVisible, null);

		// Each of the three presentation triggers alone reports the presentation visible or a request scheduled.
		Assert.IsTrue(state.IsPresentationVisibleOrRequestScheduled);
	}

	[TestMethod]
	public void IsPresentationVisibleOrRequestScheduled_WithOnlyTooltipContent_IsFalse()
	{
		// The tooltip content alone is not part of the union: it counts only while the tooltip is
		// flagged as visible.
		var state = new TextCompletionPresentationState(false, false, false, "docs");

		Assert.IsNotNull(state.TooltipContent);
		Assert.IsFalse(state.IsPresentationVisibleOrRequestScheduled);
	}
}
