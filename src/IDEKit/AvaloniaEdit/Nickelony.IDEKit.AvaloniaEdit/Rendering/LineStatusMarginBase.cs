using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;

namespace Nickelony.IDEKit.AvaloniaEdit.Rendering;

/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Class']/*"/>
/// <remarks>
/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='LineStatusMarginBase.Remarks']/*"/>
/// <para>
/// The margin reads the editor font size from the inherited <see cref="TextBlock.FontSizeProperty"/>;
/// AvaloniaEdit does not expose its text view's font size publicly, so the inherited value keeps the
/// margin proportional to the text when the host sets the font size on the editor.
/// </para>
/// </remarks>
public abstract class LineStatusMarginBase : AbstractMargin
{
	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='DesignFontSize']/*"/>
	protected const double DesignFontSize = 12.0;

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidthProperty']/*"/>
	public static readonly StyledProperty<double> MarginWidthProperty = AvaloniaProperty.Register<LineStatusMarginBase, double>(
		nameof(MarginWidth),
		defaultValue: 16.0,
		validate: ValidateMarginWidth);

	private IChangeNotificationSource? _source;

	private int _textViewConnectionVersion;
	private bool _sourceSubscribed;
	private int _invalidationQueued;

	static LineStatusMarginBase()
	{
		AffectsMeasure<LineStatusMarginBase>(MarginWidthProperty);

		// WPF publishes TextBlock.FontSize with framework-level Inherits | AffectsMeasure, so a margin
		// re-measures whenever the inherited editor font changes. Avalonia's TextElement.FontSize is
		// inherited but does not invalidate measure on its own, so the margin registers it here to keep the
		// reserved width proportional to the editor font without a host-side invalidation.
		AffectsMeasure<LineStatusMarginBase>(TextBlock.FontSizeProperty);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='IsSourceSubscribed']/*"/>
	internal bool IsSourceSubscribed => _sourceSubscribed;

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='IsInvalidationQueued']/*"/>
	internal bool IsInvalidationQueued => Volatile.Read(ref _invalidationQueued) != 0;

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='SetNotifyingSource']/*"/>
	protected void SetNotifyingSource(IChangeNotificationSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (_source is not null)
			throw new InvalidOperationException("A notifying source is already assigned.");

		_source = source;

		// A margin that is already connected when the source is registered subscribes right away;
		// without this the registration would silently never subscribe.
		UpdateSourceSubscription(TextView is not null);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='FollowSourceNotifications']/*"/>
	protected void FollowSourceNotifications(ILineStatusSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (source is IChangeNotificationSource notifyingSource)
			SetNotifyingSource(notifyingSource);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='RejectNullValue']/*"/>
	protected static bool RejectNullValue(object? value) => value is not null;

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='MarginWidth']/*"/>
	public double MarginWidth
	{
		get => GetValue(MarginWidthProperty);
		set => SetValue(MarginWidthProperty, value);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='QueueVisualInvalidation']/*"/>
	/// <remarks>
	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='QueueVisualInvalidation.Remarks']/*"/>
	/// </remarks>
	protected void QueueVisualInvalidation()
	{
		if (Interlocked.Exchange(ref _invalidationQueued, 1) != 0)
			return;

		int connectionVersion = Volatile.Read(ref _textViewConnectionVersion);

		Dispatcher.Post(
			() =>
			{
				Volatile.Write(ref _invalidationQueued, 0);

				if (connectionVersion == Volatile.Read(ref _textViewConnectionVersion) && TextView is not null)
					InvalidateVisual();
			},
			DispatcherPriority.Render);
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetFontScale']/*"/>
	protected double GetFontScale()
	{
		// AbstractMargin derives from Control, which has no FontSize property; the inherited
		// TextBlock.FontSize property supplies the editor font size.
		double fontSize = GetValue(TextBlock.FontSizeProperty);
		return fontSize > 0.0 && double.IsFinite(fontSize) ? fontSize / DesignFontSize : 1.0;
	}

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetEffectiveMarginWidth']/*"/>
	protected double GetEffectiveMarginWidth()
		=> Bounds.Width > 0.0 ? Bounds.Width : MarginWidth * GetFontScale();

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize)
		=> new(MarginWidth * GetFontScale(), 0.0);

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetMarkedLineNumbers']/*"/>
	protected abstract IReadOnlyList<int> GetMarkedLineNumbers();

	/// <include file="../../../../../shared/docs/LineStatusMarginBase.xml" path="doc/members/member[@name='DrawMarker']/*"/>
	protected internal abstract void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop);

	/// <inheritdoc/>
	protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
	{
		Interlocked.Increment(ref _textViewConnectionVersion);

		if (oldTextView is not null)
		{
			oldTextView.VisualLinesChanged -= TextView_VisualLinesChanged;
			oldTextView.ScrollOffsetChanged -= TextView_ScrollOffsetChanged;
		}

		base.OnTextViewChanged(oldTextView, newTextView);

		if (newTextView is not null)
		{
			newTextView.VisualLinesChanged += TextView_VisualLinesChanged;
			newTextView.ScrollOffsetChanged += TextView_ScrollOffsetChanged;
		}

		UpdateSourceSubscription(newTextView is not null);
		InvalidateVisual();
	}

	/// <inheritdoc/>
	public override void Render(DrawingContext context)
	{
		TextView? textView = TextView;

		if (textView is null || !textView.VisualLinesValid)
			return;

		IReadOnlyList<int>? markedLineNumbers = GetMarkedLineNumbers();

		// The contract requires a non-null result, but the read runs in the render pass, so a misbehaving
		// source must not turn into a render exception.
		if (markedLineNumbers is null || markedLineNumbers.Count == 0)
			return;

		// A source that invalidated the view during its read would make the VisualLines access below
		// throw, so the validity is re-checked after the read.
		if (!textView.VisualLinesValid)
			return;

		int markedLineIndex = 0;

		foreach (VisualLine line in textView.VisualLines)
		{
			int lineNumber = line.FirstDocumentLine.LineNumber;

			// Marked lines are ascending, so the index only moves forward as the visual lines advance.
			// A word-wrapped document line stays a single visual line with several text lines, so it is
			// drawn once; DrawMarker receives the line box's top and subclasses cover the full wrapped
			// height through VisualLine.Height.
			while (markedLineIndex < markedLineNumbers.Count && markedLineNumbers[markedLineIndex] < lineNumber)
				markedLineIndex++;

			if (markedLineIndex >= markedLineNumbers.Count)
				break;

			if (markedLineNumbers[markedLineIndex] != lineNumber)
				continue;

			double visualTop = line.VisualTop - textView.VerticalOffset;

			DrawMarker(context, line, visualTop);
		}
	}

	private static bool ValidateMarginWidth(double width)
		=> double.IsFinite(width) && width >= 0.0;

	private void UpdateSourceSubscription(bool connected)
	{
		if (_source is null)
			return;

		if (connected == _sourceSubscribed)
			return;

		_sourceSubscribed = connected;

		if (connected)
			_source.Changed += Source_Changed;
		else
			_source.Changed -= Source_Changed;
	}

	private void Source_Changed(object? sender, EventArgs e)
		=> QueueVisualInvalidation();

	private void TextView_VisualLinesChanged(object? sender, EventArgs e)
		=> InvalidateVisual();

	private void TextView_ScrollOffsetChanged(object? sender, EventArgs e)
		=> InvalidateVisual();
}
