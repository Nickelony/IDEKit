using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Completion;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;
using static Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests.CompletionTestHelpers;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

public sealed partial class CompletionPresenterTooltipTests
{
	[TestMethod]
	public void Tooltip_Theme_AppliesBaselineChromeAndHonorsConfigureTooltipHook()
	{
		ToolTip? configuredTooltip = null;

		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CompletionTestHelpers.CreateHostedController(
				hooks: new CompletionPresenterHooks
				{
					ConfigureTooltip = tooltip =>
					{
						configuredTooltip = tooltip;
						tooltip.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
						tooltip.StaysOpen = false;
					},
				},

				presentation: new CompletionPresentationOptions
				{
					WindowTheme = CompletionTestHelpers.CreateTheme(),
					TooltipTheme = CompletionTooltipTheme.Default with { Background = Brushes.Pink, BorderBrush = Brushes.Purple },
				}
			);

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);
					Assert.AreSame(tooltip, configuredTooltip);

					// The hook runs after the theme, so it can override it; the properties it does not touch keep the
					// theme's values.
					Assert.AreEqual(System.Windows.Controls.Primitives.PlacementMode.Left, tooltip.Placement);
					Assert.IsFalse(tooltip.StaysOpen);
					Assert.AreSame(Brushes.Pink, tooltip.Background);
					Assert.AreSame(Brushes.Purple, tooltip.BorderBrush);
					Assert.AreSame(completionWindow.CompletionList.ListBox, tooltip.PlacementTarget);
					Assert.AreEqual(CompletionTooltipTheme.Default.HorizontalOffset, tooltip.HorizontalOffset);
					Assert.AreEqual(new Thickness(0.0), tooltip.BorderThickness);
					Assert.AreEqual(new Thickness(0.0), tooltip.Padding);
				}
			}
		}
	}

	[TestMethod]
	public void TooltipTheme_AssignedAfterConstruction_ChromesTheNextWindowsTooltip()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					CompletionTooltipTheme theme = CompletionTooltipTheme.Default with { Background = Brushes.Pink, BorderBrush = Brushes.Purple };

					hosted.Completion.TooltipTheme = theme;

					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.AreSame(theme, hosted.Completion.TooltipTheme);
					Assert.AreSame(Brushes.Pink, tooltip?.Background);
					Assert.AreSame(Brushes.Purple, tooltip?.BorderBrush);
				}
			}
		}
	}

	[TestMethod]
	public void Tooltip_ThemeWithoutBrushes_LeavesTheThemeColorsInPlace()
	{
		(CompletionController Controller, CompletionWindowPresenter Presenter, HostWindow HostWindow, CompletionPresenter Completion) hosted =
			CreateHostedController();

		using (hosted.HostWindow)
		{
			using (hosted.Completion)
			{
				using (hosted.Controller)
				{
					Assert.IsTrue(hosted.Completion.OpenOrRefresh([CreateItem("sample")], TextRange.FromBounds(0, 5)));

					CompletionWindow completionWindow =
						hosted.Presenter.ActiveWindow ?? throw new InvalidOperationException("The completion window was not tracked.");

					Assert.IsTrue(CompletionTooltipResolver.TryGetTooltip(completionWindow, out ToolTip? tooltip));
					Assert.IsNotNull(tooltip);

					// The default theme styles placement, offset, and thickness but leaves the brushes to the WPF
					// theme, so the presenter must not set local brush values on the tooltip.
					Assert.AreEqual(CompletionTooltipTheme.Default.Placement, tooltip.Placement);
					Assert.AreEqual(CompletionTooltipTheme.Default.HorizontalOffset, tooltip.HorizontalOffset);
					Assert.IsNull(CompletionTooltipTheme.Default.Background);
					Assert.IsNull(CompletionTooltipTheme.Default.BorderBrush);
					Assert.AreEqual(DependencyProperty.UnsetValue, tooltip.ReadLocalValue(Control.BackgroundProperty));
					Assert.AreEqual(DependencyProperty.UnsetValue, tooltip.ReadLocalValue(Control.BorderBrushProperty));
				}
			}
		}
	}

	[TestMethod]
	public void IsTooltipSupported_PinsTheAvalonEditTooltipFieldAvailability()
	{
		// The public flag reports whether AvalonEdit exposes its private tooltip field; the canary fails
		// visibly when an AvalonEdit upgrade removes the field the controller decorates.
		Assert.IsTrue(CompletionPresenter.IsTooltipSupported, "The referenced AvalonEdit version no longer exposes the completion tooltip field.");
	}
}
