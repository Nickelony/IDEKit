using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Navigation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Identifies the host action a resolved definition navigation requires.</summary>
/// <remarks>
/// A target is produced by <see cref="DefinitionResolver"/> and names the host action: move to <see cref="DocumentStart"/> in the resolved document,
/// or open the other document named by <see cref="Location"/>. <see cref="DocumentStart"/> is validated against the resolved document, so a host need
/// not re-check it; the only tolerance is a column past the end of its line, which the host clamps when applying the position.
/// </remarks>
public sealed record DefinitionNavigationTarget
{
	/// <param name="location">The resolved definition location.</param>
	/// <param name="documentStart">
	/// The validated zero-based position to navigate to inside the requested document, or <see langword="null"/> when the location names
	/// another document.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="location"/> is <see langword="null"/>.</exception>
	public DefinitionNavigationTarget(DefinitionLocation location, TextPosition? documentStart = null)
	{
		ArgumentNullException.ThrowIfNull(location);

		Location = location;
		DocumentStart = documentStart;
	}

	/// <summary>The resolved definition location.</summary>
	public DefinitionLocation Location { get; }

	/// <summary>
	/// The validated zero-based position to navigate to inside the requested document, or <see langword="null"/> when the definition is located
	/// in another document.
	/// </summary>
	public TextPosition? DocumentStart { get; }
}
