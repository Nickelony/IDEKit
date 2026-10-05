using Nickelony.QuickPick;

namespace Nickelony.Testing;

/// <summary>
/// Builds a quick-pick controller over modes and a pumped UI thread, and records every state the controller published and every fault it reported,
/// so a test states the user's input and reads the rows without a UI toolkit.
/// </summary>
/// <remarks>
/// The host disposes its controller and its UI thread, which restores the synchronization context of the test thread. A provider that answers
/// on a pool thread is waited for by <see cref="Open"/>, <see cref="Type"/> and <see cref="Refresh"/>, which pump the UI thread
/// until the answer arrived.
/// </remarks>
internal sealed class QuickPickTestHost : IDisposable
{
	private QuickPickController? _controller;

	/// <param name="providers">The providers of the controller, each offered under its own prefix, in registration order.</param>
	public QuickPickTestHost(params IQuickPickProvider[] providers)
		: this([.. providers.Select(provider => new QuickPickMode(provider))]) { }

	private QuickPickTestHost(IReadOnlyList<QuickPickMode> modes)
	{
		Modes = modes;
		Dispatcher = new FakePumpedUiThread(installSynchronizationContext: true);
	}

	/// <summary>Creates a host over modes that carry a prefix of their own.</summary>
	/// <param name="modes">The modes of the controller, in registration order.</param>
	public static QuickPickTestHost ForModes(params QuickPickMode[] modes) => new(modes);

	/// <summary>The UI thread the controller runs on; the test pumps it and moves its clock.</summary>
	public FakePumpedUiThread Dispatcher { get; }

	public IReadOnlyList<QuickPickMode> Modes { get; }

	public IReadOnlyList<IQuickPickProvider> Providers => [.. Modes.Select(mode => mode.Provider)];

	/// <summary>The faults the controller reported, in order.</summary>
	public List<Exception> Faults { get; } = [];

	/// <summary>The states the controller published, in order.</summary>
	public List<QuickPickPresentationState> States { get; } = [];

	/// <summary>The controller of the host, created on first use with the default options and no store.</summary>
	public QuickPickController Controller => _controller ?? CreateController();

	/// <summary>The rows of the last published state; a separator shows as <c>--label--</c>, and a message as <c>!message</c>.</summary>
	public IReadOnlyList<string> RowTexts => [.. Controller.State.Rows.Select(Describe)];

	/// <summary>The labels of the items of the rows of the last published state, without the separators.</summary>
	public IReadOnlyList<string> Labels => [.. Controller.State.Rows.Where(row => row.Item is not null).Select(row => row.Item!.Label)];

	/// <summary>The keys of the items of the rows of the last published state, without the separators.</summary>
	public IReadOnlyList<string> Keys => [.. Controller.State.Rows.Where(row => row.Item is not null).Select(row => row.Item!.Key)];

	/// <summary>Creates the controller of the host and starts recording its states.</summary>
	public QuickPickController CreateController(QuickPickControllerOptions? options = null, IQuickPickRecentStore? recentStore = null)
	{
		_controller?.Dispose();
		States.Clear();

		_controller = new QuickPickController(
			modes: [.. Modes],
			recentStore,
			reportFault: Faults.Add,
			options,
			timeProvider: Dispatcher.TimeProvider
		);

		_controller.Changed += (_, _) => States.Add(_controller.State);

		return _controller;
	}

	/// <summary>Opens a session and waits for the first answer.</summary>
	public void Open(string text = "")
	{
		Controller.Open(text);
		WaitForAnswer(previousRows: null);
	}

	/// <summary>Types a text and waits for the answer to it.</summary>
	/// <param name="text">The new text of the input.</param>
	/// <param name="rest">How long the mode waits before it queries, or <see langword="null"/> for the delay of the default options.</param>
	public void Type(string text, TimeSpan? rest = null)
	{
		IReadOnlyList<QuickPickRow> before = Controller.State.Rows;

		Controller.SetText(text);
		PassDebounce(rest);
		WaitForAnswer(before);
	}

	/// <summary>Asks for the list again and waits for the new answer.</summary>
	public void Refresh()
	{
		IReadOnlyList<QuickPickRow> before = Controller.State.Rows;

		Controller.Refresh();
		WaitForAnswer(before);
	}

	/// <summary>Accepts the active item, pumping the UI thread until the accept ended.</summary>
	/// <returns>The result of the accept.</returns>
	public bool Accept()
	{
		Task<bool> accept = Controller.AcceptAsync();

		Dispatcher.PumpUntil(() => accept.IsCompleted);

		return accept.Result;
	}

	/// <summary>
	/// Moves the clock past the debounce delay of the default options, or past <paramref name="rest"/>, and runs what that releases.
	/// </summary>
	/// <param name="rest">
	/// How long a mode with a delay of its own waits before it queries, or <see langword="null"/> for the delay of the default options.
	/// </param>
	public void PassDebounce(TimeSpan? rest = null) => Dispatcher.Advance(rest ?? QuickPickControllerOptions.Default.QueryDebounceDelay);

	public void Dispose()
	{
		_controller?.Dispose();
		Dispatcher.Dispose();
	}

	private void WaitForAnswer(IReadOnlyList<QuickPickRow>? previousRows) =>
		Dispatcher.PumpUntil(() => Controller.State.Rows.Count > 0 && !ReferenceEquals(Controller.State.Rows, previousRows));

	private static string Describe(QuickPickRow row)
	{
		return row.Item is { } item ? item.Label
			: row.Message is { } message ? $"!{message}"
			: $"--{row.SeparatorLabel}--";
	}
}
