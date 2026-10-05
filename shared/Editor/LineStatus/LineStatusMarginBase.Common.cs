using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Notifications;
using Nickelony.IDEKit.Core.Validation;
#if AVALONIAEDIT
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
#else
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
#endif

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

#else

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

#endif

public abstract partial class LineStatusMarginBase
{
	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='DesignFontSize']/*"/>
	protected const double DesignFontSize = 12.0;

	private readonly LineStatusInvalidationGate _invalidationGate;

	protected LineStatusMarginBase() => _invalidationGate = CreateInvalidationGate();

	/// <summary>Creates the gate that queues invalidations, because only the engine half knows the engine's dispatcher.</summary>
	/// <returns>The gate that posts through the margin's dispatcher.</returns>
	private partial LineStatusInvalidationGate CreateInvalidationGate();

	/// <summary>Reads the editor font size from the engine's inherited font-size property.</summary>
	/// <returns>The font size in device-independent units.</returns>
	private partial double GetEditorFontSize();

	/// <summary>Reads the width the margin was last arranged at.</summary>
	/// <returns>The arranged width, or zero before the first arrange pass.</returns>
	private partial double GetArrangedWidth();

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='IsSourceSubscribed']/*"/>
	internal bool IsSourceSubscribed => _invalidationGate.IsSourceSubscribed;

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='IsInvalidationQueued']/*"/>
	internal bool IsInvalidationQueued => _invalidationGate.IsInvalidationQueued;

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='SetNotifyingSource']/*"/>
	protected void SetNotifyingSource(IChangeNotificationSource source)
	{
		ArgumentNullException.ThrowIfNull(source);
		_invalidationGate.SetSource(source);
	}

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='FollowSourceNotifications']/*"/>
	protected void FollowSourceNotifications(ILineStatusSource source)
	{
		ArgumentNullException.ThrowIfNull(source);

		if (source is IChangeNotificationSource notifyingSource)
			SetNotifyingSource(notifyingSource);
	}

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='RejectNullValue']/*"/>
	protected static bool RejectNullValue(object? value) => value is not null;

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetFontScale']/*"/>
	protected double GetFontScale()
	{
		double fontSize = GetEditorFontSize();
		return NumericValidation.IsFinitePositive(fontSize) ? fontSize / DesignFontSize : 1.0;
	}

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetEffectiveMarginWidth']/*"/>
	protected double GetEffectiveMarginWidth() => GetArrangedWidth() > 0.0 ? GetArrangedWidth() : MarginWidth * GetFontScale();

	/// <inheritdoc/>
	protected override Size MeasureOverride(Size availableSize) => new(MarginWidth * GetFontScale(), 0.0);

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='QueueVisualInvalidation']/*"/>
	/// <remarks>
	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='QueueVisualInvalidation.Remarks']/*"/>
	/// </remarks>
	protected void QueueVisualInvalidation() => _invalidationGate.QueueInvalidation();

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='GetMarkedLineNumbers']/*"/>
	protected abstract IReadOnlyList<int> GetMarkedLineNumbers();

	/// <include file="../../docs/LineStatusMarginBase.xml" path="doc/members/member[@name='DrawMarker']/*"/>
	protected internal abstract void DrawMarker(DrawingContext drawingContext, VisualLine visualLine, double visualTop);

	/// <inheritdoc/>
#if AVALONIAEDIT
	protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
#else
	protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
#endif
	{
		_invalidationGate.BumpConnectionVersion();

		if (oldTextView is not null)
		{
			oldTextView.VisualLinesChanged -= HandleVisualLinesChanged;
			oldTextView.ScrollOffsetChanged -= HandleScrollOffsetChanged;
		}

		base.OnTextViewChanged(oldTextView, newTextView);

		if (newTextView is not null)
		{
			newTextView.VisualLinesChanged += HandleVisualLinesChanged;
			newTextView.ScrollOffsetChanged += HandleScrollOffsetChanged;
		}

		_invalidationGate.UpdateSubscription(newTextView is not null);
		InvalidateVisual();
	}

	private void HandleVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

	private void HandleScrollOffsetChanged(object? sender, EventArgs e) => InvalidateVisual();
}
