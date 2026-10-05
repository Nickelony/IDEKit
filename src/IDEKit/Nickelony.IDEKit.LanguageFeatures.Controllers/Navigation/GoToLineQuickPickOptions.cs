namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Navigation;

/// <summary>Carries the texts of the go to line mode.</summary>
/// <remarks>
/// Start from <see cref="Default"/> and override only the options needed with a <c>with</c> expression; each option validates itself when assigned.
/// A text that holds numbers is a composite format string, and the numbers are formatted with the current culture.
/// </remarks>
public sealed record GoToLineQuickPickOptions
{
	private string _name = "Go to line";
	private string _placeholder = "Type a line number, or line:column, to go to";
	private string _noEditorText = "Open a file to go to a line in it.";
	private string _hintText = "Current line: {0}, column: {1}. Type a line number between 1 and {2} to go to it.";
	private string _lineText = "Go to line {0}";
	private string _lineColumnText = "Go to line {0}, column {1}";

	/// <summary>The options with the default of every option.</summary>
	public static GoToLineQuickPickOptions Default { get; } = new();

	/// <summary>The name of the mode, which the list of modes shows; the default is "Go to line".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Name
	{
		get => _name;
		init => _name = value ?? throw new ArgumentNullException(nameof(Name));
	}

	/// <summary>
	/// The hint shown in the empty text box while the mode is active; the default is "Type a line number, or line:column, to go to".
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string Placeholder
	{
		get => _placeholder;
		init => _placeholder = value ?? throw new ArgumentNullException(nameof(Placeholder));
	}

	/// <summary>The text of the dimmed row that tells no editor is open; the default is "Open a file to go to a line in it.".</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string NoEditorText
	{
		get => _noEditorText;
		init => _noEditorText = value ?? throw new ArgumentNullException(nameof(NoEditorText));
	}

	/// <summary>The text of the dimmed row that tells the position of the caret and the range of lines while the typed text is no position.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	/// <remarks>
	/// The default is "Current line: {0}, column: {1}. Type a line number between 1 and {2} to go to it.", where <c>{0}</c> is the line of the caret,
	/// <c>{1}</c> its column and <c>{2}</c> the number of lines of the document.
	/// </remarks>
	public string HintText
	{
		get => _hintText;
		init => _hintText = value ?? throw new ArgumentNullException(nameof(HintText));
	}

	/// <summary>The text of the row that names a line; the default is "Go to line {0}", where <c>{0}</c> is the line.</summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string LineText
	{
		get => _lineText;
		init => _lineText = value ?? throw new ArgumentNullException(nameof(LineText));
	}

	/// <summary>
	/// The text of the row that names a line and a column; the default is "Go to line {0}, column {1}", where <c>{0}</c> is the line and <c>{1}</c>
	/// the column.
	/// </summary>
	/// <exception cref="ArgumentNullException">The assigned value is <see langword="null"/>.</exception>
	public string LineColumnText
	{
		get => _lineColumnText;
		init => _lineColumnText = value ?? throw new ArgumentNullException(nameof(LineColumnText));
	}
}
