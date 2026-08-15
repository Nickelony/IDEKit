using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Rendering;
using Nickelony.IDEKit.AvaloniaEdit.Rendering;
using Nickelony.IDEKit.Infrastructure;

namespace Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures.CodeActions;

/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='TextCodeActionMargin.Class']/*"/>
public sealed class TextCodeActionMargin : LineStatusIconMarginBase
{
	private static readonly Geometry s_defaultIconGeometry = CreateIconGeometry();
	private static readonly ImmutableSolidColorBrush s_defaultIconBrush = BrushHelpers.CreateFrozenBrush(Color.FromRgb(0x77, 0x77, 0x77));

	// The immutable brush keeps a theme-neutral default visible at design time while leaving a host's style
	// setter free to override it (a dynamic resource reference would outrank style setters); hosts that want
	// the brush to follow theme changes assign their own theme brush.
	static TextCodeActionMargin()
	{
		IconBrushProperty.OverrideDefaultValue<TextCodeActionMargin>(s_defaultIconBrush);
		IconGeometryProperty.OverrideDefaultValue<TextCodeActionMargin>(s_defaultIconGeometry);
	}

	private readonly TextCodeActionController _controller;

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='OpenOnLeftPressProperty']/*"/>
	public static readonly StyledProperty<bool> OpenOnLeftPressProperty = AvaloniaProperty.Register<TextCodeActionMargin, bool>(
		nameof(OpenOnLeftPress),
		defaultValue: true);

	/// <include file="../../../../../shared/docs/TextCodeActionMargin.xml" path="doc/members/member[@name='OpenOnLeftPress']/*"/>
	public bool OpenOnLeftPress
	{
		get => GetValue(OpenOnLeftPressProperty);
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
	protected override Point ResolveClickPosition(PointerPressedEventArgs e)
		=> TestHooks.ClickPositionResolver is Func<PointerPressedEventArgs, Point> resolver
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
			context.BeginFigure(new Point(5.5, 0.8), true);
			context.ArcTo(new Point(9.9, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true);
			context.ArcTo(new Point(5.5, 9.6), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true);
			context.ArcTo(new Point(1.1, 5.2), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true);
			context.ArcTo(new Point(5.5, 0.8), new Size(4.4, 4.4), 0.0, false, SweepDirection.Clockwise, true);
			context.EndFigure(true);

			context.BeginFigure(new Point(4.4, 8.6), true);
			context.LineTo(new Point(6.6, 8.6), true);
			context.LineTo(new Point(6.0, 11.6), true);
			context.LineTo(new Point(5.0, 11.6), true);
			context.EndFigure(true);
		}

		return geometry;
	}
}
