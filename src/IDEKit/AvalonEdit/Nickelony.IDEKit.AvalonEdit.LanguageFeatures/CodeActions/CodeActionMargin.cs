using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using Nickelony.IDEKit.AvalonEdit.LineStatus;
using Nickelony.IDEKit.AvalonEdit.Media;
using Nickelony.IDEKit.Core.LineStatus;
using Nickelony.IDEKit.Core.Media;
using Nickelony.IDEKit.LanguageFeatures.CodeActions;
using Nickelony.IDEKit.LanguageFeatures.Controllers.CodeActions;

namespace Nickelony.IDEKit.AvalonEdit.LanguageFeatures.CodeActions;

/// <include file="../../../../../shared/docs/CodeActionMargin.xml" path="doc/members/member[@name='CodeActionMargin.Class']/*"/>
public sealed class CodeActionMargin : LineStatusIconMarginBase
{
	private static readonly Geometry s_defaultIconGeometry = CreateIconGeometry();
	private static readonly SolidColorBrush s_defaultIconBrush = ArgbColor.FromRgb(0x77, 0x77, 0x77).ToBrush();

	static CodeActionMargin()
	{
		IconBrushProperty.OverrideMetadata(
			typeof(CodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconBrush, FrameworkPropertyMetadataOptions.AffectsRender)
		);

		IconGeometryProperty.OverrideMetadata(
			typeof(CodeActionMargin),
			new FrameworkPropertyMetadata(s_defaultIconGeometry, FrameworkPropertyMetadataOptions.AffectsRender)
		);
	}

	private readonly ILineStatusSource _source;
	private readonly CodeActionPresenter _presenter;

	/// <include file="../../../../../shared/docs/CodeActionMargin.xml" path="doc/members/member[@name='OpensOnLeftClickProperty']/*"/>
	public static readonly DependencyProperty OpensOnLeftClickProperty = DependencyProperty.Register(
		name: nameof(OpensOnLeftClick),
		propertyType: typeof(bool),
		ownerType: typeof(CodeActionMargin),
		typeMetadata: new FrameworkPropertyMetadata(true)
	);

	/// <include file="../../../../../shared/docs/CodeActionMargin.xml" path="doc/members/member[@name='OpensOnLeftClick']/*"/>
	public bool OpensOnLeftClick
	{
		get => (bool)GetValue(OpensOnLeftClickProperty);
		set => SetValue(OpensOnLeftClickProperty, value);
	}

	internal CodeActionMargin(ILineStatusSource source, CodeActionPresenter presenter)
	{
		_source = source;
		_presenter = presenter;
		FollowSourceNotifications(source);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers() => _source.GetMarkedLineNumbers();

	/// <inheritdoc/>
	protected override bool OnIconClicked(VisualLine visualLine, Point position)
	{
		// The host owns the gesture when it disabled the default one; the press stays untouched.
		if (!OpensOnLeftClick)
			return false;

		return _presenter.TryOpenMenuFromMargin(visualLine.FirstDocumentLine.LineNumber, position);
	}

	private static StreamGeometry CreateIconGeometry()
	{
		var geometry = new StreamGeometry();

		using (StreamGeometryContext context = geometry.Open())
		{
			// The glass is a circle; the stem is a tapering trapezoid that overlaps the glass, so the
			// circle and stem read as one bulb silhouette.
			context.BeginFigure(new Point(5.5, 0.8), true, true);

			context.ArcTo(
				point: new Point(9.9, 5.2),
				size: new Size(4.4, 4.4),
				rotationAngle: 0.0,
				isLargeArc: false,
				sweepDirection: SweepDirection.Clockwise,
				isStroked: true,
				isSmoothJoin: false
			);

			context.ArcTo(
				point: new Point(5.5, 9.6),
				size: new Size(4.4, 4.4),
				rotationAngle: 0.0,
				isLargeArc: false,
				sweepDirection: SweepDirection.Clockwise,
				isStroked: true,
				isSmoothJoin: false
			);

			context.ArcTo(
				point: new Point(1.1, 5.2),
				size: new Size(4.4, 4.4),
				rotationAngle: 0.0,
				isLargeArc: false,
				sweepDirection: SweepDirection.Clockwise,
				isStroked: true,
				isSmoothJoin: false
			);

			context.ArcTo(
				point: new Point(5.5, 0.8),
				size: new Size(4.4, 4.4),
				rotationAngle: 0.0,
				isLargeArc: false,
				sweepDirection: SweepDirection.Clockwise,
				isStroked: true,
				isSmoothJoin: false
			);

			context.BeginFigure(new Point(4.4, 8.6), true, true);

			context.LineTo(new Point(6.6, 8.6), true, false);
			context.LineTo(new Point(6.0, 11.6), true, false);
			context.LineTo(new Point(5.0, 11.6), true, false);
		}

		geometry.Freeze();
		return geometry;
	}
}
