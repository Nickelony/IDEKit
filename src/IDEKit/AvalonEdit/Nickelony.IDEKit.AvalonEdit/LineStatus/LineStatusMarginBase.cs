using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Class']/*"/>
/// <remarks>
/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Remarks']/*"/>
/// </remarks>
public abstract partial class LineStatusMarginBase : AbstractMargin
{
	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidthProperty']/*"/>
	public static readonly DependencyProperty MarginWidthProperty = DependencyProperty.Register(
		name: nameof(MarginWidth),
		propertyType: typeof(double),
		ownerType: typeof(LineStatusMarginBase),
		typeMetadata: new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure),
		validateValueCallback: ValidateMarginWidth
	);

	// WPF's Dispatcher exposes shutdown probes, so the gate stops posting once it is shutting down. A shutdown that races the post releases the
	// queue gate, so a later invalidation is not suppressed.
	private partial LineStatusInvalidationGate CreateInvalidationGate()
	{
		return new(
			isConnected: () => TextView is not null,
			invalidate: InvalidateVisual,
			post: action => Dispatcher.BeginInvoke(DispatcherPriority.Render, action),
			canPost: () => !(Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
		);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidth']/*"/>
	public double MarginWidth
	{
		get => (double)GetValue(MarginWidthProperty);
		set => SetValue(MarginWidthProperty, value);
	}

	// AbstractMargin derives from FrameworkElement, which has no FontSize property;
	// the inherited TextBlock.FontSize attached property supplies the editor font size.
	private partial double GetEditorFontSize() => (double)GetValue(TextBlock.FontSizeProperty);

	private partial double GetArrangedWidth() => ActualWidth;

	/// <inheritdoc/>
	protected override void OnRender(DrawingContext drawingContext)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
			return;

		MarkedLineWalker.DrawMarkers(context: drawingContext, textView, markedLineNumbers: GetMarkedLineNumbers(), DrawMarker);
	}

	private static bool ValidateMarginWidth(object value) => value is double width && NumericValidation.IsFiniteNonNegative(width);
}
