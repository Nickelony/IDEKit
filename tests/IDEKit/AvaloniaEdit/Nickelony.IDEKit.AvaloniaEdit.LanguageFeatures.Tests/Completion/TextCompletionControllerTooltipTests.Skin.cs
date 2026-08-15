using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Completion;
using static Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_Skin_IsNotAppliedAndTheConfigureTooltipHookIsNotInvoked()
	{
		ToolTip? configuredTooltip = null;
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted =
			CompletionTestHost.CreateHostedController(hooks: new TextCompletionControllerHooks
			{
				TooltipSkin = CompletionTooltipSkin.Default with
				{
					Background = Brushes.Pink,
					BorderBrush = Brushes.Purple
				},
				ConfigureTooltip = tooltip => configuredTooltip = tooltip
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				// Avalonia divergence: AvaloniaEdit exposes no tooltip, so the skin and the hook are skipped
				// and the hook receives no tooltip to configure. The reference asserted the hook's overrides
				// on the tooltip; here the hook must simply never run.
				Assert.IsNull(configuredTooltip);
				AssertTooltipPathDisabled(hosted.Controller, completionWindow);
			}
		}
	}

	[TestMethod]
	public void Tooltip_SkinDefaults_AreTheRecordValues()
	{
		// Avalonia divergence: there is no tooltip to read the applied chrome from, so the defaults are
		// pinned on the record itself; the presenter's skip path leaves them unused at runtime.
		Assert.AreEqual(PlacementMode.Right, CompletionTooltipSkin.Default.Placement);
		Assert.AreEqual(10.0, CompletionTooltipSkin.Default.HorizontalOffset);
		Assert.IsNull(CompletionTooltipSkin.Default.Background);
		Assert.IsNull(CompletionTooltipSkin.Default.BorderBrush);
		Assert.AreEqual(new Thickness(0.0), CompletionTooltipSkin.Default.BorderThickness);
		Assert.AreEqual(new Thickness(0.0), CompletionTooltipSkin.Default.Padding);
	}

	[TestMethod]
	public void IsTooltipSupported_PinsThatAvaloniaEditExposesNoTooltipMember()
	{
		// The public flag is the package's version-compat probe for AvalonEdit's private tooltip field.
		// Avalonia divergence: AvaloniaEdit publishes no tooltip member, so the probe is always false, and
		// this canary fails visibly if an AvaloniaEdit upgrade starts exposing one the mirror must adopt.
		Assert.IsFalse(
			TextCompletionController.IsTooltipSupported,
			"The referenced AvaloniaEdit version now exposes a completion tooltip member; adopt it instead of degrading.");
	}
}
