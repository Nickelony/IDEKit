using Nickelony.IDEKit.Core.LineStatus;

#if AVALONIAEDIT

namespace Nickelony.IDEKit.AvaloniaEdit.LineStatus;

#else

namespace Nickelony.IDEKit.AvalonEdit.LineStatus;

#endif

public sealed partial class ChangeMarkerMargin
{
	// The sample marker color (dodger blue) and the 4-DIP bar width that suits a diff gutter instead of the 16-DIP reserved width of the base class.
	private const byte SampleMarkerRed = 0x1E;
	private const byte SampleMarkerGreen = 0x90;
	private const byte SampleMarkerBlue = 0xFF;

	private const double BarWidth = 4.0;

	private readonly ILineStatusSource _markerSource;

	/// <include file="../../docs/ChangeMarkerMargin.xml" path="doc/members/member[@name='ChangeMarkerMargin']/*"/>
	public ChangeMarkerMargin(ILineStatusSource markerSource)
	{
		ArgumentNullException.ThrowIfNull(markerSource);
		_markerSource = markerSource;

		FollowSourceNotifications(markerSource);
	}

	/// <inheritdoc/>
	protected override IReadOnlyList<int> GetMarkedLineNumbers() => _markerSource.GetMarkedLineNumbers();
}
