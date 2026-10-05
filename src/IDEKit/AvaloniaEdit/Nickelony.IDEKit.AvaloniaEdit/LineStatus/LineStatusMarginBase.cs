using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Class']/*"/>
/// <remarks>
/// <para>
/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Remarks']/*"/>
/// </para>
/// <para>
/// The margin reads the editor font size from the inherited <see cref="TextBlock.FontSizeProperty"/>; AvaloniaEdit does not expose its text view's
/// font size publicly, so the inherited value keeps the margin proportional to the text when the host sets the font size on the editor.
/// </para>
/// </remarks>
public abstract partial class LineStatusMarginBase : AbstractMargin
{
	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidthProperty']/*"/>
	public static readonly StyledProperty<double> MarginWidthProperty = AvaloniaProperty.Register<LineStatusMarginBase, double>(
		nameof(MarginWidth),
		defaultValue: 16.0,
		validate: ValidateMarginWidth
	);

	// Avalonia's Dispatcher exposes no shutdown probe, so the gate keeps the default canPost. Its IDispatcher.Post returns void, so a post that a
	// shutting-down dispatcher never runs cannot be detected and the gate is released only by the queued action itself; a stuck gate is harmless,
	// because a shut-down dispatcher runs no further render pass.
	private partial LineStatusInvalidationGate CreateInvalidationGate()
	{
		return new(
			isConnected: () => TextView is not null,
			invalidate: InvalidateVisual,
			post: action => Dispatcher.Post(action, DispatcherPriority.Render)
		);
	}

	static LineStatusMarginBase()
	{
		AffectsMeasure<LineStatusMarginBase>(MarginWidthProperty);

		// Avalonia's inherited FontSize does not invalidate measure on its own, so the margin registers it
		// here to keep the reserved width proportional to the editor font without a host-side invalidation.
		AffectsMeasure<LineStatusMarginBase>(TextBlock.FontSizeProperty);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidth']/*"/>
	public double MarginWidth
	{
		get => GetValue(MarginWidthProperty);
		set => SetValue(MarginWidthProperty, value);
	}

	// AbstractMargin derives from Control, which has no FontSize property; the inherited
	// TextBlock.FontSize property supplies the editor font size.
	private partial double GetEditorFontSize() => GetValue(TextBlock.FontSizeProperty);

	private partial double GetArrangedWidth() => Bounds.Width;

	/// <inheritdoc/>
	public override void Render(DrawingContext context)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
			return;

		MarkedLineWalker.DrawMarkers(context, textView, markedLineNumbers: GetMarkedLineNumbers(), DrawMarker);
	}

	private static bool ValidateMarginWidth(double width) => NumericValidation.IsFiniteNonNegative(width);
}
