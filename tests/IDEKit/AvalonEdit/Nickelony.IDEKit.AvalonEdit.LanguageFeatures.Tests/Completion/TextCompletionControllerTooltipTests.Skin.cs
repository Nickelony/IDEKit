using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHost;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class TextCompletionControllerTooltipTests
{
	[TestMethod]
	public void Tooltip_Skin_AppliesBaselineChromeAndHonorsConfigureTooltipHook()
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
				ConfigureTooltip = tooltip =>
				{
					configuredTooltip = tooltip;
					tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
					tooltip.StaysOpen = false;
				}
			});

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);
				Assert.AreSame(tooltip, configuredTooltip);

				// The hook runs after the skin, so it can override it; the properties it does not touch keep the
				// skin's values.
				Assert.AreEqual(System.Windows.Controls.Primitives.PlacementMode.Left, tooltip.Placement);
				Assert.IsFalse(tooltip.StaysOpen);
				Assert.AreSame(Brushes.Pink, tooltip.Background);
				Assert.AreSame(Brushes.Purple, tooltip.BorderBrush);
				Assert.AreSame(completionWindow.CompletionList.ListBox, tooltip.PlacementTarget);
				Assert.AreEqual(CompletionTooltipSkin.Default.HorizontalOffset, tooltip.HorizontalOffset);
				Assert.AreEqual(new Thickness(0.0), tooltip.BorderThickness);
				Assert.AreEqual(new Thickness(0.0), tooltip.Padding);
			}
		}
	}

	[TestMethod]
	public void Tooltip_SkinWithoutBrushes_LeavesTheThemeColorsInPlace()
	{
		(TextCompletionController Controller, CompletionWindowCoordinator Coordinator, HostWindow HostWindow) hosted = CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Controller)
			{
				Assert.IsTrue(hosted.Controller.OpenOrRefresh([CreateItem("sample")], 0, 5));

				CompletionWindow completionWindow = hosted.Coordinator.ActiveWindow
					?? throw new InvalidOperationException("The completion window was not tracked.");

				Assert.IsTrue(CompletionWindowTooltipAccess.TryGetTooltip(completionWindow, out ToolTip? tooltip));
				Assert.IsNotNull(tooltip);

				// The default skin styles placement, offset, and thickness but leaves the brushes to the WPF
				// theme, so the presenter must not set local brush values on the tooltip.
				Assert.AreEqual(CompletionTooltipSkin.Default.Placement, tooltip.Placement);
				Assert.AreEqual(CompletionTooltipSkin.Default.HorizontalOffset, tooltip.HorizontalOffset);
				Assert.IsNull(CompletionTooltipSkin.Default.Background);
				Assert.IsNull(CompletionTooltipSkin.Default.BorderBrush);
				Assert.AreEqual(DependencyProperty.UnsetValue, tooltip.ReadLocalValue(Control.BackgroundProperty));
				Assert.AreEqual(DependencyProperty.UnsetValue, tooltip.ReadLocalValue(Control.BorderBrushProperty));
			}
		}
	}

	[TestMethod]
	public void IsTooltipSupported_PinsTheAvalonEditTooltipFieldAvailability()
	{
		// The public flag is the package's version-compat probe for AvalonEdit's private tooltip field; the
		// canary fails visibly when an AvalonEdit upgrade removes the field the controller decorates.
		Assert.IsTrue(TextCompletionController.IsTooltipSupported, "The referenced AvalonEdit version no longer exposes the completion tooltip field.");
	}
}
