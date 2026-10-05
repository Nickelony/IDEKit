using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.Tests;

/// <summary>Provides the Avalonia half of the menu and margin helpers that the shared code-action tests call.</summary>
internal static class CodeActionTestHelpers
{
	/// <summary>Asserts that the menu closes when the user interacts outside of it.</summary>
	/// <param name="menu">The code-action menu.</param>
	/// <remarks>
	/// The <c>ContextMenu</c> is always light-dismissed, so there is no property to assert; the light-dismiss behavior is covered
	/// by the external-close test instead of by a menu property.
	/// </remarks>
	internal static void AssertMenuDismissesOnOutsideInteraction(ContextMenu menu) { }

	/// <summary>Asserts that the menu is placed relative to an explicit anchor.</summary>
	/// <param name="menu">The code-action menu.</param>
	/// <remarks>
	/// The menu is placed with an explicit anchor and gravity: the requested anchor becomes the top-left with a bottom-right gravity.
	/// </remarks>
	internal static void AssertMenuPlacedAtAnchor(ContextMenu menu) => Assert.AreEqual(PlacementMode.AnchorAndGravity, menu.Placement);

	/// <summary>Sets the tooltip of a menu item.</summary>
	/// <param name="item">The menu item.</param>
	/// <param name="text">The tooltip text.</param>
	/// <remarks>
	/// A tooltip is an attached property (<c>ToolTip.Tip</c>), so the helper sets it through <c>ToolTip.SetTip</c>.
	/// </remarks>
	internal static void SetItemTooltip(MenuItem item, string text) => ToolTip.SetTip(item, text);

	/// <summary>Gets the tooltip of a menu item.</summary>
	/// <param name="item">The menu item.</param>
	/// <returns>The tooltip content.</returns>
	internal static object? GetItemTooltip(MenuItem item) => ToolTip.GetTip(item);

	/// <summary>Closes the menu the way the user does, without the presenter asking.</summary>
	/// <param name="menu">The code-action menu.</param>
	/// <remarks>
	/// The <c>ContextMenu.IsOpen</c> setter is not public, so the external close is issued through <c>Close</c>, which raises the same <c>Closed</c>
	/// event a light-dismiss would.
	/// </remarks>
	internal static void CloseFromOutside(ContextMenu menu) => menu.Close();

	/// <summary>Asserts the default indicator of a margin: the neutral gray brush and a library geometry.</summary>
	/// <param name="margin">The code-action margin.</param>
	/// <remarks>
	/// Avalonia has no <c>FrameworkPropertyMetadata</c> render flag. The static constructor registers the icon properties as render-affecting,
	/// which is the render-invalidation contract, so the helper asserts no flag.
	/// </remarks>
	internal static void AssertDefaultIndicator(CodeActionMargin margin)
	{
		var defaultBrush = (IBrush)CodeActionMargin.IconBrushProperty.GetDefaultValue(typeof(CodeActionMargin))!;
		var defaultGeometry = (Geometry)CodeActionMargin.IconGeometryProperty.GetDefaultValue(typeof(CodeActionMargin))!;

		// The default indicator color is the explicit neutral gray both bindings ship (Avalonia has no
		// SystemColors to derive one from) - a theme-neutral default visible in common themes rather than a
		// host-specific accent color.
		Assert.AreEqual(Color.FromRgb(0x77, 0x77, 0x77), ((ISolidColorBrush)defaultBrush).Color);

		// Avalonia has no Freeze; the default is an immutable (frozen-equivalent) brush.
		Assert.IsInstanceOfType<ImmutableSolidColorBrush>(defaultBrush);

		Assert.IsGreaterThan(0.0, defaultGeometry.Bounds.Width);
		Assert.IsGreaterThan(0.0, defaultGeometry.Bounds.Height);
		Assert.AreSame(margin.IconBrush, defaultBrush);
		Assert.AreSame(margin.IconGeometry, defaultGeometry);
	}

	/// <summary>Raises a left-button press on a margin that no text area hosts and asserts that nothing handles it.</summary>
	/// <param name="margin">The unattached code-action margin.</param>
	/// <remarks>
	/// An unattached margin resolves no visual line, so the routed press runs through without opening anything and, with no editor
	/// above it to consume the press, leaves the event unhandled.
	/// </remarks>
	internal static void AssertUnattachedPressIsIgnored(CodeActionMargin margin)
	{
		(PointerPressedEventArgs Args, bool HandledByMargin) raise = RaisePointerPressed(margin, default, PointerUpdateKind.LeftButtonPressed);

		Assert.IsFalse(raise.HandledByMargin);
		Assert.IsFalse(raise.Args.Handled);
	}

	/// <summary>Gets the width of the strip that the margin occupies, in whole pixels.</summary>
	/// <param name="margin">The laid-out code-action margin.</param>
	/// <returns>The width, at least one pixel.</returns>
	internal static int GetStripWidth(CodeActionMargin margin) => Math.Max(1, (int)Math.Ceiling(margin.Bounds.Width));

	/// <summary>Raises a pointer press on the element with the supplied pointer kind, exercising the margin's routed press path.</summary>
	/// <param name="element">
	/// The element that receives the press; it is the root visual of the event, so the root position is its own position.
	/// </param>
	/// <param name="position">The press position.</param>
	/// <param name="pointerUpdateKind">The pointer kind that the press reports.</param>
	/// <returns>The event arguments together with the handled state that the margin's own class handler left behind.</returns>
	/// <remarks>
	/// AvaloniaEdit's <c>TextArea</c> consumes the press as it bubbles past the margin, so the final handled state of the arguments
	/// reflects the whole route rather than the margin. The margin's class handler runs before an instance handler on the same element,
	/// and <c>handledEventsToo</c> keeps the snapshot handler running even once the press is handled.
	/// </remarks>
	internal static (PointerPressedEventArgs Args, bool HandledByMargin) RaisePointerPressed(
		Interactive element,
		Point position,
		PointerUpdateKind pointerUpdateKind
	)
	{
		RawInputModifiers modifiers =
			pointerUpdateKind == PointerUpdateKind.LeftButtonPressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton;

		var eventArgs = new PointerPressedEventArgs(
			source: element,
			pointer: new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
			rootVisual: element,
			rootVisualPosition: position,
			timestamp: 0UL,
			properties: new PointerPointProperties(modifiers, pointerUpdateKind),
			modifiers: KeyModifiers.None
		);

		bool handledByMargin = false;

		element.AddHandler(
			routedEvent: InputElement.PointerPressedEvent,
			handler: (_, e) => handledByMargin = e.Handled,
			routes: RoutingStrategies.Bubble,
			handledEventsToo: true
		);

		element.RaiseEvent(eventArgs);

		return (eventArgs, handledByMargin);
	}
}
