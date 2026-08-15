using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.IntelliSense.Navigation;

/// <summary>
/// Identifies the host action a resolved definition navigation requires.
/// </summary>
/// <remarks>
/// <para>
/// A target is produced by <see cref="TextDefinitionNavigator"/> and describes which of the two
/// host-owned actions applies: moving to <see cref="DocumentStart"/> inside the document the lookup
/// was resolved against, or opening the other document named by <see cref="Location"/>.
/// </para>
/// <para>
/// <see cref="DocumentStart"/> is the zero-based position of
/// <see cref="TextDefinitionLocation.NavigationStart"/>, validated against the document the request was
/// resolved from. It is <see langword="null"/> exactly when the location names another document (a
/// non-<see langword="null"/> <see cref="TextDefinitionLocation.DocumentId"/>): such a location is not
/// validated against a document its coordinates do not address. The navigator never produces an
/// in-document target whose start falls outside the document, so a host applies
/// <see cref="DocumentStart"/> without repeating the position checks; a column past the end of its line
/// is the one tolerance and is clamped by the host when it applies the position.
/// </para>
/// </remarks>
public sealed record TextDefinitionNavigationTarget
{
	/// <summary>
	/// Initializes a new instance of the <see cref="TextDefinitionNavigationTarget"/> record.
	/// </summary>
	/// <param name="location">The resolved definition location.</param>
	/// <param name="documentStart">
	/// The validated zero-based position to navigate to inside the requested document, or
	/// <see langword="null"/> when the location names another document.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
	public TextDefinitionNavigationTarget(TextDefinitionLocation location, TextPosition? documentStart = null)
	{
		ArgumentNullException.ThrowIfNull(location);

		Location = location;
		DocumentStart = documentStart;
	}

	/// <summary>
	/// Gets the resolved definition location.
	/// </summary>
	public TextDefinitionLocation Location { get; }

	/// <summary>
	/// Gets the validated zero-based position to navigate to inside the requested document, or
	/// <see langword="null"/> when the definition is located in another document.
	/// </summary>
	public TextPosition? DocumentStart { get; }
}
