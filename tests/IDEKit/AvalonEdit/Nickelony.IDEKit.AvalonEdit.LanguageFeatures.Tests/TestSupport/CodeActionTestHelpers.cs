using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.Tests;

/// <summary>Provides the WPF half of the menu and margin helpers that the shared code-action tests call.</summary>
internal static class CodeActionTestHelpers
{
	/// <summary>Asserts that the menu closes when the user interacts outside of it.</summary>
	/// <param name="menu">The code-action menu.</param>
	internal static void AssertMenuDismissesOnOutsideInteraction(ContextMenu menu) => Assert.IsFalse(menu.StaysOpen);

	/// <summary>Asserts that the menu is placed relative to an explicit anchor.</summary>
	/// <param name="menu">The code-action menu.</param>
	internal static void AssertMenuPlacedAtAnchor(ContextMenu menu) => Assert.AreEqual(PlacementMode.RelativePoint, menu.Placement);

	/// <summary>Sets the tooltip of a menu item.</summary>
	/// <param name="item">The menu item.</param>
	/// <param name="text">The tooltip text.</param>
	internal static void SetItemTooltip(MenuItem item, string text) => item.ToolTip = text;

	/// <summary>Gets the tooltip of a menu item.</summary>
	/// <param name="item">The menu item.</param>
	/// <returns>The tooltip content.</returns>
	internal static object? GetItemTooltip(MenuItem item) => item.ToolTip;

	/// <summary>Closes the menu the way the user does, without the presenter asking.</summary>
	/// <param name="menu">The code-action menu.</param>
	internal static void CloseFromOutside(ContextMenu menu) => menu.IsOpen = false;

	/// <summary>Asserts the default indicator of a margin: the neutral gray brush, a library geometry and the render invalidation.</summary>
	/// <param name="margin">The code-action margin.</param>
	internal static void AssertDefaultIndicator(CodeActionMargin margin)
	{
		var brushMetadata = (FrameworkPropertyMetadata)CodeActionMargin.IconBrushProperty.GetMetadata(typeof(CodeActionMargin));
		var geometryMetadata = (FrameworkPropertyMetadata)CodeActionMargin.IconGeometryProperty.GetMetadata(typeof(CodeActionMargin));

		// The default indicator color is the same explicit neutral gray both bindings ship - a theme-neutral
		// default visible in common themes - instead of a host-specific accent color.
		var defaultBrush = (SolidColorBrush)brushMetadata.DefaultValue;

		Assert.AreEqual(Color.FromRgb(0x77, 0x77, 0x77), defaultBrush.Color);
		Assert.IsTrue(defaultBrush.IsFrozen);

		var geometry = (Geometry)geometryMetadata.DefaultValue;

		Assert.IsGreaterThan(0.0, geometry.Bounds.Width);
		Assert.IsGreaterThan(0.0, geometry.Bounds.Height);
		Assert.AreSame(margin.IconBrush, defaultBrush);
		Assert.AreSame(margin.IconGeometry, geometry);

		Assert.IsTrue(brushMetadata.AffectsRender);
		Assert.IsTrue(geometryMetadata.AffectsRender);
	}

	/// <summary>Raises a left-button press on a margin that no text area hosts and asserts that nothing handles it.</summary>
	/// <param name="margin">The unattached code-action margin.</param>
	internal static void AssertUnattachedPressIsIgnored(CodeActionMargin margin)
	{
		var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent };

		margin.RaiseEvent(args);

		Assert.IsFalse(args.Handled);
	}

	/// <summary>Gets the width of the strip that the margin occupies, in whole pixels.</summary>
	/// <param name="margin">The laid-out code-action margin.</param>
	/// <returns>The width, at least one pixel.</returns>
	internal static int GetStripWidth(CodeActionMargin margin) => Math.Max(1, (int)Math.Ceiling(margin.ActualWidth));
}
