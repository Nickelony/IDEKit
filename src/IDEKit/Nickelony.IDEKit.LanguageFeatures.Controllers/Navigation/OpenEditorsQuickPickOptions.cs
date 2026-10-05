using Nickelony.IDEKit.Core.Navigation;
using Nickelony.IDEKit.Core.Paths;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>
/// Carries the choices of the open editors mode: how it shows the folder of a document, how it marks an unsaved one, and the texts it shows.
/// </summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned.
/// </remarks>
public sealed record OpenEditorsQuickPickOptions
{
	private IReadOnlyList<string> _rootPaths = [];

	private string _name = "Open editors";
	private string _placeholder = "Select an open editor to switch to";
	private string _emptyText = "No matching open editors";

	/// <summary>The options with the default of every option.</summary>
	public static OpenEditorsQuickPickOptions Default { get; } = new();

	/// <summary>The name of the mode, which the list of modes shows; the default is "Open editors".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Name
	{
		get => _name;
		init => _name = value ?? throw new ArgumentNullException(nameof(Name));
	}

	/// <summary>The hint shown in the empty text box while the mode is active; the default is "Select an open editor to switch to".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The text of the row that a query without a match leaves; the default is "No matching open editors".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string EmptyText
	{
		get => _emptyText;
		init => _emptyText = value ?? throw new ArgumentNullException(nameof(EmptyText));
	}

	/// <summary>The workspace roots, as normalized paths, below which the mode shows the folder of a document.</summary>
	/// <exception cref="ArgumentNullException">The assigned list, or an entry of it, is <see langword="null"/>.</exception>
	/// <remarks>
	/// A document below a root shows its folder below that root, written with slashes, with the name of the root in front
	/// when there are several roots. A document outside every root, and a document without a file, shows the folder of its id.
	/// </remarks>
	public IReadOnlyList<string> RootPaths
	{
		get => _rootPaths;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			foreach (string root in value)
				ArgumentNullException.ThrowIfNull(root, nameof(RootPaths));

			_rootPaths = [.. value];
		}
	}

	/// <summary>The comparison that decides which roots hold a document.</summary>
	public LocalPathComparisonPolicy PathComparison { get; init; } = LocalPathComparisonPolicy.ForCurrentPlatform;

	/// <summary>The text shown at the right edge of a row whose document has unsaved changes, or <see langword="null"/> to show none.</summary>
	/// <remarks>
	/// The item of an unsaved document also carries <see cref="OpenEditorDescriptor.UnsavedIconKey"/>, which a host maps to an image.
	/// A host that shows another language assigns its own text here.
	/// </remarks>
	public string? UnsavedText { get; init; } = "unsaved";
}
