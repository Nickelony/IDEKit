using Nickelony.IDEKit.Core.Navigation;
using Nickelony.QuickPick;

namespace Nickelony.IDEKit.Workspace.QuickPick;

/// <summary>
/// Carries the choices of a <see cref="WorkspaceFileQuickPickProvider"/>: which files stay out of the list, how many it holds,
/// and what the mode shows.
/// </summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned.
/// </remarks>
public sealed record WorkspaceFileQuickPickOptions
{
	private IReadOnlyList<string> _excludeGlobs = [];
	private readonly int _maxFileCount = 50_000;

	private string _name = "Files";
	private string _placeholder = "Type the name of a file to open";
	private string _emptyText = "No matching files";

	/// <summary>The options with the default of every option.</summary>
	public static WorkspaceFileQuickPickOptions Default { get; } = new();

	/// <summary>
	/// The globs of the files and folders that stay out of the list, such as <c>bin</c>, <c>.git</c>, <c>*.tmp</c> or <c>**/docs/build</c>.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned list, or an entry of it, is <see langword="null"/>.</exception>
	/// <exception cref="ArgumentException">An entry of the assigned list is empty or whitespace.</exception>
	/// <remarks>
	/// <para>
	/// A glob uses the wildcards <c>*</c>, which matches any run of characters within one name, and <c>?</c>,
	/// which matches one. A glob without a slash matches the name of a file or a folder at any depth, so <c>bin</c> excludes every folder
	/// of that name with everything below it.
	/// </para>
	/// <para>
	/// A glob with a slash matches the path below the root, written with slashes, one segment at a time,
	/// and its segment <c>**</c> matches zero or more segments of the path. So <c>docs/build</c> matches only the folder of that path at the root,
	/// <c>src/*/generated</c> matches the folders <c>generated</c> that lie one folder below <c>src</c>, <c>src/**/obj</c> matches <c>obj</c>
	/// at any depth below <c>src</c>, and a leading <c>**/</c>, as in <c>**/bin/**</c>, lets the rest match at any depth.
	/// A trailing <c>/**</c> adds nothing, because a folder excludes everything below it.
	/// </para>
	/// <para>
	/// Casing follows the path comparison that the provider receives.
	/// </para>
	/// </remarks>
	public IReadOnlyList<string> ExcludeGlobs
	{
		get => _excludeGlobs;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			foreach (string glob in value)
			{
				ArgumentNullException.ThrowIfNull(glob);
				ArgumentException.ThrowIfNullOrWhiteSpace(glob);
			}

			_excludeGlobs = [.. value];
		}
	}

	/// <summary>The largest number of files the list holds across all roots; the default is 50,000.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not positive.</exception>
	/// <remarks>
	/// The roots are walked in the order the provider got them, and each takes what the earlier ones left, so an earlier root can fill the list
	/// and leave none for a later one. A file that is created while the list is full is ignored. Within a root, the files that the walk meets
	/// first stay.
	/// </remarks>
	public int MaxFileCount
	{
		get => _maxFileCount;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
			_maxFileCount = value;
		}
	}

	/// <summary>The text shown at the right edge of a row whose document has unsaved changes, or <see langword="null"/> to show none.</summary>
	/// <remarks>
	/// The default is "unsaved", the text of the open editors mode, so the two modes agree.
	/// The row also carries <see cref="OpenEditorDescriptor.UnsavedIconKey"/>, which a host maps to an image.
	/// </remarks>
	public string? UnsavedText { get; init; } = "unsaved";

	/// <summary>The name of the mode, which the list of modes shows; the default is "Files".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Name
	{
		get => _name;
		init => _name = value ?? throw new ArgumentNullException(nameof(Name));
	}

	/// <summary>The hint shown in the empty text box while the mode is active; the default is "Type the name of a file to open".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The text of the row that a query without a match leaves; the default is "No matching files".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string EmptyText
	{
		get => _emptyText;
		init => _emptyText = value ?? throw new ArgumentNullException(nameof(EmptyText));
	}

	/// <summary>
	/// The text of the separator above the recently opened files and the open documents,
	/// or <see langword="null"/> to draw none; the default is "recently opened".
	/// </summary>
	public string? RecentGroupLabel { get; init; } = "recently opened";

	/// <summary>The text of the separator above the other files, or <see langword="null"/> to draw none; the default is "file results".</summary>
	public string? FileGroupLabel { get; init; } = "file results";
}
