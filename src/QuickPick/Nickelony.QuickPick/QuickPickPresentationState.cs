namespace Nickelony.QuickPick;

/// <summary>Describes what a quick pick shows at one moment: the text, the rows, the active row and whether a query is slow.</summary>
/// <remarks>
/// The controller publishes a new value on every change and never mutates one, so a presenter reads the value
/// that <c>QuickPickController.Changed</c> announced. A host that drives the controller without the popup draws its own window from it.
/// </remarks>
public sealed record QuickPickPresentationState
{
	private string _text = string.Empty;
	private string _placeholder = string.Empty;

	private IReadOnlyList<QuickPickRow> _rows = [];

	/// <summary>The state of a quick pick that is not open.</summary>
	public static QuickPickPresentationState Closed { get; } = new();

	/// <summary>Whether a session is open, which a presenter shows as a visible window.</summary>
	public bool IsOpen { get; init; }

	/// <summary>The text of the input, including the prefix that picks the mode.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Text
	{
		get => _text;
		init => _text = value ?? throw new ArgumentNullException(nameof(Text));
	}

	/// <summary>The hint to show while <see cref="Text"/> is empty, which is the placeholder of the active mode.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The rows to list, in order: the separators of the groups and the items under them.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public IReadOnlyList<QuickPickRow> Rows
	{
		get => _rows;
		init => _rows = value ?? throw new ArgumentNullException(nameof(Rows));
	}

	/// <summary>The index in <see cref="Rows"/> of the active row, or <c>-1</c> when no item can be active.</summary>
	public int ActiveRowIndex { get; init; } = -1;

	/// <summary>Whether the query has been running longer than the busy delay, which a presenter shows as a progress bar.</summary>
	public bool IsBusy { get; init; }

	/// <summary>The active item, or <see langword="null"/> when no item is active.</summary>
	public QuickPickItem? ActiveItem => ActiveRowIndex >= 0 && ActiveRowIndex < Rows.Count ? Rows[ActiveRowIndex].Item : null;
}
