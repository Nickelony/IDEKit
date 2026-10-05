using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Core.Validation;

namespace Nickelony.IDEKit.LanguageFeatures.Controllers.WorkspaceSymbols;

/// <summary>
/// Carries the choices of the workspace symbol mode: how it shows the document that declares a symbol, how long it waits for typing, and its texts.
/// </summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned.
/// </remarks>
public sealed record WorkspaceSymbolQuickPickOptions
{
	private IReadOnlyList<string> _rootPaths = [];
	private TimeSpan _queryDebounceDelay = TimeSpan.FromMilliseconds(200.0);

	private string _name = "Go to symbol in workspace";
	private string _placeholder = "Type the name of a symbol to open";
	private string _emptyText = "No matching workspace symbols";
	private string _containerSeparator = " • ";

	/// <summary>The options with the default of every option.</summary>
	public static WorkspaceSymbolQuickPickOptions Default { get; } = new();

	/// <summary>The workspace roots, as normalized paths, below which the mode shows the path of a document.</summary>
	/// <exception cref="ArgumentNullException">The assigned list, or an entry of it, is <see langword="null"/>.</exception>
	/// <remarks>
	/// A document below a root shows its path below that root, written with slashes, with the name of the root in front when there are several roots.
	/// A document outside every root shows its own path.
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

	/// <summary>The comparison that decides which roots hold a document, and which documents are one.</summary>
	public LocalPathComparisonPolicy PathComparison { get; init; } = LocalPathComparisonPolicy.ForCurrentPlatform;

	/// <summary>How long typed text rests before the mode asks the servers; the default is 200 milliseconds, as in VS Code.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Every query costs a request to each server, so the mode waits longer than the delay of the controller, which suits a list in memory.
	/// </remarks>
	public TimeSpan QueryDebounceDelay
	{
		get => _queryDebounceDelay;
		init => _queryDebounceDelay = NumericValidation.NonNegative(value, nameof(QueryDebounceDelay));
	}

	/// <summary>The name of the mode, which the list of modes shows; the default is "Go to symbol in workspace".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Name
	{
		get => _name;
		init => _name = value ?? throw new ArgumentNullException(nameof(Name));
	}

	/// <summary>The hint shown in the empty text box while the mode is active; the default is "Type the name of a symbol to open".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The text of the row that a query without a match leaves; the default is "No matching workspace symbols".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string EmptyText
	{
		get => _emptyText;
		init => _emptyText = value ?? throw new ArgumentNullException(nameof(EmptyText));
	}

	/// <summary>
	/// The text that joins the name of the container and the path of the document in the description of a row; the default is a bullet
	/// between spaces.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string ContainerSeparator
	{
		get => _containerSeparator;
		init => _containerSeparator = value ?? throw new ArgumentNullException(nameof(ContainerSeparator));
	}
}
