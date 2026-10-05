using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Nickelony.Samples.Avalonia;

/// <summary>The find and replace window: it edits the search of the workbench and runs its find and replace commands on the active pane.</summary>
internal sealed class SampleFindWindow : Window
{
	private readonly SampleWorkbench _workbench;

	private readonly TextBox _find = new() { MinWidth = 280 };
	private readonly TextBox _replace = new() { MinWidth = 280 };

	private readonly CheckBox _matchCase = new() { Content = "Match _case" };
	private readonly CheckBox _wholeWord = new() { Content = "Match _whole word" };
	private readonly CheckBox _regex = new() { Content = "Use _regular expressions" };

	private readonly TextBlock _result = new() { Opacity = 0.7, Margin = new Thickness(left: 0, top: 8, right: 0, bottom: 0) };

	/// <param name="workbench">The workbench whose search the window edits.</param>
	internal SampleFindWindow(SampleWorkbench workbench)
	{
		_workbench = workbench;

		Title = "Find and replace";
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		SizeToContent = SizeToContent.WidthAndHeight;
		CanResize = false;
		ShowInTaskbar = false;

		_find.Text = workbench.Search.FindText;
		_replace.Text = workbench.ReplaceText;
		_matchCase.IsChecked = workbench.Search.MatchCase;
		_wholeWord.IsChecked = workbench.Search.MatchWholeWord;
		_regex.IsChecked = workbench.Search.UseRegex;

		_find.TextChanged += (_, _) => UpdateSearch();
		_replace.TextChanged += (_, _) => workbench.ReplaceText = _replace.Text ?? string.Empty;

		foreach (CheckBox option in new[] { _matchCase, _wholeWord, _regex })
			option.IsCheckedChanged += (_, _) => UpdateSearch();

		// The window shows the outcome of each command it runs, which the main status line shows too.
		void showResult(object? _, StatusChangedEventArgs e) => _result.Text = e.Message;
		workbench.StatusChanged += showResult;
		Closed += (_, _) => workbench.StatusChanged -= showResult;

		KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter)
				_workbench.FindNext();
			else if (e.Key == Key.Escape)
				Close();
		};

		var buttons = new WrapPanel { Margin = new Thickness(left: 0, top: 10, right: 0, bottom: 0) };

		buttons.Children.Add(CreateButton("Find _previous", () => _workbench.FindPrevious()));
		buttons.Children.Add(CreateButton("Find _next", () => _workbench.FindNext()));
		buttons.Children.Add(CreateButton("Find _all", _workbench.FindAll));
		buttons.Children.Add(CreateButton("Find in _files", _workbench.FindInFiles));
		buttons.Children.Add(CreateButton("Replace a_ll", _workbench.ReplaceAll));

		var fields = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };

		AddField(grid: fields, row: 0, label: "Find", box: _find);
		AddField(grid: fields, row: 1, label: "Replace", box: _replace);

		var options = new StackPanel { Spacing = 4, Margin = new Thickness(left: 0, top: 8, right: 0, bottom: 0) };

		options.Children.Add(_matchCase);
		options.Children.Add(_wholeWord);
		options.Children.Add(_regex);

		var layout = new StackPanel { Margin = new Thickness(14) };

		layout.Children.Add(fields);
		layout.Children.Add(options);
		layout.Children.Add(buttons);
		layout.Children.Add(_result);

		Content = layout;

		Opened += (_, _) => _find.Focus();
	}

	/// <summary>Puts a selected single-line text in the find box, the way editors start a search from the selection.</summary>
	internal void Prefill(string selectedText)
	{
		if (selectedText.Length > 0 && !selectedText.Contains('\n'))
			_find.Text = selectedText;

		_find.SelectAll();
	}

	private static void AddField(Grid grid, int row, string label, TextBox box)
	{
		var text = new TextBlock
		{
			Text = label,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(left: 0, top: 4, right: 10, bottom: 4),
		};

		Grid.SetRow(text, row);
		Grid.SetRow(box, row);

		Grid.SetColumn(box, 1);

		grid.Children.Add(text);
		grid.Children.Add(box);
	}

	private static Button CreateButton(string caption, Action execute)
	{
		var button = new Button { Content = caption, Margin = new Thickness(left: 0, top: 0, right: 6, bottom: 0) };
		button.Click += (_, _) => execute();

		return button;
	}

	private void UpdateSearch()
	{
		_workbench.Search = new SampleSearchOptions(
			FindText: _find.Text ?? string.Empty,
			UseRegex: _regex.IsChecked == true,
			MatchCase: _matchCase.IsChecked == true,
			MatchWholeWord: _wholeWord.IsChecked == true
		);
	}
}
