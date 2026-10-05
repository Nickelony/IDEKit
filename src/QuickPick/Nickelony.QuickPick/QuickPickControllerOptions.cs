namespace Nickelony.QuickPick;

/// <summary>Stores the timing, the bounds, the help mode and the texts of the list of modes of a quick-pick controller.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned,
/// so an invalid value fails at the offending expression rather than at controller construction.
/// </remarks>
public sealed record QuickPickControllerOptions
{
	private readonly TimeSpan _queryDebounceDelay = TimeSpan.FromMilliseconds(50.0);
	private readonly TimeSpan _busyDelay = TimeSpan.FromMilliseconds(150.0);

	private readonly int _maxItemCount = 1000;
	private readonly int _maxRecentCount = 50;

	private readonly string _helpModePlaceholder = "Select a mode";
	private readonly string _helpModeEmptyText = "No matching results";

	/// <summary>
	/// The default options: a 50-millisecond query debounce delay, a 150-millisecond busy delay, 1000 items, 50 recent keys and the help mode on.
	/// </summary>
	public static QuickPickControllerOptions Default { get; } = new();

	/// <summary>The debounce delay before typed text starts a query; the first query of a session starts at once.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// Typing restarts the delay, so a run of keystrokes asks the provider once.
	/// </remarks>
	public TimeSpan QueryDebounceDelay
	{
		get => _queryDebounceDelay;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
			_queryDebounceDelay = value;
		}
	}

	/// <summary>How long a query runs before the presentation state reports it as busy.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is negative.</exception>
	/// <remarks>
	/// A query that answers sooner never shows the busy state, so a fast provider does not flicker a progress bar.
	/// </remarks>
	public TimeSpan BusyDelay
	{
		get => _busyDelay;
		init
		{
			ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
			_busyDelay = value;
		}
	}

	/// <summary>The largest number of items the list shows; the controller drops the lowest-ranked items beyond it.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not positive.</exception>
	public int MaxItemCount
	{
		get => _maxItemCount;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
			_maxItemCount = value;
		}
	}

	/// <summary>The number of recently accepted keys the controller keeps for each mode.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The assigned value is not positive.</exception>
	public int MaxRecentCount
	{
		get => _maxRecentCount;
		init
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
			_maxRecentCount = value;
		}
	}

	/// <summary>Whether the text that starts with <c>?</c> lists the modes; defaults to <see langword="true"/>.</summary>
	/// <remarks>
	/// When the help mode is off, <c>?</c> is no reserved prefix: a mode may claim it, and a text that starts with it is filtered as plain text
	/// by the mode that the rest of the text picks. A quick pick without the help mode needs a mode with the empty prefix, because a text that starts
	/// with no prefix must pick a mode.
	/// </remarks>
	public bool EnableHelpMode { get; init; } = true;

	/// <summary>The hint shown in the empty text box while the list of modes is active; the default is "Select a mode".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string HelpModePlaceholder
	{
		get => _helpModePlaceholder;
		init => _helpModePlaceholder = value ?? throw new ArgumentNullException(nameof(HelpModePlaceholder));
	}

	/// <summary>The text of the row that a query without a matching mode leaves in the list of modes; the default is "No matching results".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string HelpModeEmptyText
	{
		get => _helpModeEmptyText;
		init => _helpModeEmptyText = value ?? throw new ArgumentNullException(nameof(HelpModeEmptyText));
	}
}
