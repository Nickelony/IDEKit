using System.Windows;
using System.Windows.Controls;
using Nickelony.KeyBindings;
using Nickelony.KeyBindings.Wpf;

namespace Nickelony.Samples.Wpf;

/// <summary>
/// The key bindings window: it lists the commands with their shortcuts, records a new shortcut from a key press and applies or resets it.
/// </summary>
internal sealed class SampleKeyBindingsWindow : Window
{
	private readonly SampleWorkbench _workbench;
	private readonly ListBox _commands = new() { Height = 320 };

	private readonly TextBox _recorder = new()
	{
		IsReadOnly = true,
		MinWidth = 220,
		ToolTip = "Press a shortcut",
	};

	private readonly TextBlock _result = new() { Opacity = 0.7, Margin = new Thickness(left: 0, top: 8, right: 0, bottom: 0) };

	private KeyStroke? _recorded;

	/// <param name="workbench">The workbench whose commands and key bindings the window shows.</param>
	internal SampleKeyBindingsWindow(SampleWorkbench workbench)
	{
		_workbench = workbench;

		Title = "Key bindings";
		Width = 520;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		ShowInTaskbar = false;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;

		// The adapter turns the key event into a toolkit-free stroke, which is what the key binding service stores.
		_recorder.PreviewKeyDown += (_, e) =>
		{
			if (e.ToKeyStroke() is { } stroke)
			{
				_recorded = stroke;
				_recorder.Text = workbench.KeyBindings.GetDisplayText(stroke);
			}

			e.Handled = true;
		};

		var buttons = new WrapPanel { Margin = new Thickness(left: 0, top: 8, right: 0, bottom: 0) };

		buttons.Children.Add(CreateButton("_Apply", () => Apply(takeOver: false)));
		buttons.Children.Add(CreateButton("_Take over", () => Apply(takeOver: true)));
		buttons.Children.Add(CreateButton("_Reset", () => Run(id => _workbench.KeyBindings.Reset(id))));
		buttons.Children.Add(CreateButton("Reset a_ll", () => Show(_workbench.KeyBindings.ResetAll())));

		var layout = new StackPanel { Margin = new Thickness(14) };

		layout.Children.Add(_commands);

		layout.Children.Add(
			new TextBlock { Text = "New shortcut for the selected command", Margin = new Thickness(left: 0, top: 10, right: 0, bottom: 4) }
		);

		layout.Children.Add(_recorder);
		layout.Children.Add(buttons);
		layout.Children.Add(_result);

		Content = layout;

		workbench.KeyBindings.BindingsChanged += HandleBindingsChanged;
		Closed += (_, _) => workbench.KeyBindings.BindingsChanged -= HandleBindingsChanged;
		ShowCommands();
	}

	private static Button CreateButton(string caption, Action execute)
	{
		var button = new Button
		{
			Content = caption,
			Margin = new Thickness(left: 0, top: 0, right: 6, bottom: 0),
			Padding = new Thickness(left: 8, top: 2, right: 8, bottom: 2),
		};

		button.Click += (_, _) => execute();

		return button;
	}

	private void HandleBindingsChanged(object? sender, BindingsChangedEventArgs<string> e) => ShowCommands();

	private void ShowCommands()
	{
		int selected = _commands.SelectedIndex;
		_commands.Items.Clear();

		foreach (SampleCommand command in _workbench.Commands)
		{
			var shortcut = new TextBlock { Text = _workbench.KeyBindings.GetDisplayText(command.Id), Opacity = 0.7 };

			var row = new DockPanel
			{
				Tag = command.Id,
				LastChildFill = true,
				Width = 460,
			};

			DockPanel.SetDock(shortcut, Dock.Right);

			row.Children.Add(shortcut);
			row.Children.Add(new TextBlock { Text = command.Caption.Replace("_", string.Empty, StringComparison.Ordinal) });
			_commands.Items.Add(row);
		}

		_commands.SelectedIndex = selected;
	}

	private void Apply(bool takeOver)
	{
		if (_recorded is not { } stroke)
		{
			Show("Press a shortcut in the box first");
			return;
		}

		Run(id => _workbench.KeyBindings.Rebind(id, stroke, takeOver));
	}

	private void Run(Func<string, string> action)
	{
		if (_commands.SelectedItem is DockPanel { Tag: string commandId })
			Show(action(commandId));
		else
			Show("Select a command first");
	}

	private void Show(string message) => _result.Text = message;
}
