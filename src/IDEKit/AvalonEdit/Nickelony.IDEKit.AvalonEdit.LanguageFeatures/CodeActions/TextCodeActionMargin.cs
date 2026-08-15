using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='TextCodeActionMargin.Class']/*"/>
public sealed class TextCodeActionMargin : LineStatusIconMarginBase
{
	private static readonly Geometry s_defaultIconGeometry = CreateIconGeometry();
	private static readonly SolidColorBrush s_defaultIconBrush = BrushHelpers.CreateFrozenBrush(SystemColors.GrayTextColor);

	// The frozen brush keeps the system default visible at design time while leaving a host's style setter
	// free to override it (a dynamic resource reference would outrank style setters); hosts that want the
	// brush to follow theme changes assign their own theme brush.
	static TextCodeActionMargin()
	{
		IconBrushProperty.OverrideMetadata(
			typeof(TextCodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconBrush, FrameworkPropertyMetadataOptions.AffectsRender));
		IconGeometryProperty.OverrideMetadata(
			typeof(TextCodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconGeometry, FrameworkPropertyMetadataOptions.AffectsRender));
	}

	private readonly TextCodeActionController _controller;

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='OpenOnLeftPressProperty']/*"/>
	public static readonly DependencyProperty OpenOnLeftPressProperty = DependencyProperty.Register(
		nameof(OpenOnLeftPress),
		typeof(bool),
		typeof(TextCodeActionMargin),
		new FrameworkPropertyMetadata(true));

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='OpenOnLeftPress']/*"/>
	public bool OpenOnLeftPress
	{
		get => (bool)GetValue(OpenOnLeftPressProperty);
		set => SetValue(OpenOnLeftPressProperty, value);
	}

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='TextCodeActionMargin']/*"/>
	public TextCodeActionMargin(TextCodeActionController controller)
	{
		ArgumentNullException.ThrowIfNull(controller);
		_controller = controller;

		SetNotifyingSource(controller);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers()
		=> _controller.GetIndicatorLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		// The host owns the gesture when it disabled the default one; the press stays untouched.
		if (!OpenOnLeftPress)
			return false;

		return _controller.TryOpenActionsFromMargin(visualLine.FirstDocumentLine.LineNumber, position);
	}

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='TestHooks']/*"/>
	internal TextCodeActionMarginTestHooks TestHooks { get; } = new();

	/// <inheritdoc/>
	protected override Point ResolveClickPosition(MouseButtonEventArgs e)
		=> TestHooks.ClickPositionResolver is Func<MouseButtonEventArgs, Point> resolver
			? resolver(e)
			: base.ResolveClickPosition(e);

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='TryOpenActionsAt']/*"/>
	internal bool TryOpenActionsAt(Point position)
		=> TryGetVisualLineAt(position, out VisualLine? visualLine)
			&& _controller.TryOpenActionsFromMargin(visualLine.FirstDocumentLine.LineNumber, position);

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			// The glass is a circle; the stem is a tapering trapezoid that overlaps the glass, so the
			// two figures read as one bulb silhouette.
			context.BeginFigure(new Point(5.5, 0.8), true, true);
			context.ArcTo(new Point(9.9, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(5.5, 9.6), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(1.1, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);
			context.ArcTo(new Point(5.5, 0.8), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true, false);

			context.BeginFigure(new Point(4.4, 8.6), true, true);
			context.LineTo(new Point(6.6, 8.6), true, false);
			context.LineTo(new Point(6.0, 11.6), true, false);
			context.LineTo(new Point(5.0, 11.6), true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}
