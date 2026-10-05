#if AVALONIAQUICKPICK
using Avalonia.Controls;

namespace Nickelony.QuickPick.Avalonia.Tests;

#else
using System.Windows.Controls;

namespace Nickelony.QuickPick.Wpf.Tests;

#endif

/// <summary>
/// Shows a host element and a second focusable control in a window, and builds a quick-pick controller and presenter over a stub provider,
/// so the presenter tests share one setup.
/// </summary>
/// <remarks>
/// The window is active, because the presenter closes its window when the host window is inactive. The controller waits a millisecond
/// before it queries typed text, so a test that types pumps the dispatcher until the rows arrive.
/// </remarks>
internal sealed class QuickPickPresenterTestHost : IDisposable
{
	/// <summary>The options of most tests: the defaults with a one-millisecond query delay and a short busy delay.</summary>
	internal static readonly QuickPickControllerOptions s_fastOptions = QuickPickControllerOptions.Default with
	{
		QueryDebounceDelay = TimeSpan.FromMilliseconds(1.0),
		BusyDelay = TimeSpan.FromMilliseconds(5.0),
	};

	/// <param name="provider">The provider of the only mode.</param>
	/// <param name="theme">The theme of the presenter, or <see langword="null"/> for the default.</param>
	/// <param name="hooks">The hooks of the presenter, or <see langword="null"/> for none.</param>
	internal QuickPickPresenterTestHost(StubQuickPickProvider provider, QuickPickTheme? theme = null, QuickPickPresenterHooks? hooks = null)
	{
		Provider = provider;
		Host = new Border { Height = 300.0 };
		Other = new TextBox();
		Window = TestHost.ShowInHostWindow(CreateContent(Host, Other));
		TestWindowActivation.Activate(Window);

		TestHost.EnsureUiContext();

		Controller = new QuickPickController([new QuickPickMode(provider)], options: s_fastOptions);
		Presenter = new QuickPickPresenter(Host, Controller, theme, hooks);
	}

	internal StubQuickPickProvider Provider { get; }

	/// <summary>The element the presenter places its window against.</summary>
	internal Border Host { get; }

	/// <summary>A control that takes the keyboard focus away from the window.</summary>
	internal TextBox Other { get; }

	internal HostWindow Window { get; }

	internal QuickPickController Controller { get; }

	internal QuickPickPresenter Presenter { get; }

	/// <summary>Opens a session and runs the dispatcher work that follows, such as the request for the keyboard focus.</summary>
	/// <param name="text">The text of the input.</param>
	internal void Open(string text = "")
	{
		Controller.Open(text);
		DispatcherTestHelpers.PumpUntilIdle();
	}

	/// <summary>Types a text into the input and pumps the dispatcher until the controller answered it.</summary>
	/// <param name="text">The new text of the input.</param>
	internal void Type(string text)
	{
		Presenter.Input.Text = text;

		DispatcherTestHelpers.PumpUntil(() => Provider.Queries.Count > 0 && Provider.Queries[^1] == text);
		DispatcherTestHelpers.PumpUntilIdle();
	}

	/// <summary>Gives the labels of the rows the controller published, a separator shown as its text between dashes.</summary>
	internal IReadOnlyList<string> RowTexts =>
		[.. Controller.State.Rows.Select(row => row.Item is null ? $"--{row.SeparatorLabel}--" : row.Item.Label)];

	public void Dispose()
	{
		Presenter.Dispose();
		Controller.Dispose();
		Window.Dispose();
	}

	private static StackPanel CreateContent(Border host, TextBox other)
	{
		var panel = new StackPanel();

		panel.Children.Add(host);
		panel.Children.Add(other);

		return panel;
	}
}
