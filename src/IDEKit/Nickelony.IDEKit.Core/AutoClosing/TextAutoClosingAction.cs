namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>
/// Describes an auto-closing action.
/// </summary>
/// <remarks>
/// A default-initialized value has <see cref="TextAutoClosingActionKind.None"/> and a
/// <see langword="null"/> <see cref="ClosingText"/>; it is the failure result of
/// <see cref="TextAutoClosingResolver.TryResolveAction"/> and nothing is applied for it. Create
/// instances through the factory methods or the constructor; an insert or skip action without
/// closing text cannot be constructed.
/// </remarks>
public readonly record struct TextAutoClosingAction
{
	/// <summary>
	/// Initializes an auto-closing action.
	/// </summary>
	/// <param name="kind">The kind of auto-closing action.</param>
	/// <param name="closingText">
	/// The text to insert or skip, or <see langword="null"/> for
	/// <see cref="TextAutoClosingActionKind.None"/>.
	/// </param>
	/// <exception cref="ArgumentException">
	/// <paramref name="kind"/> is <see cref="TextAutoClosingActionKind.InsertClosingText"/> or
	/// <see cref="TextAutoClosingActionKind.SkipExistingClosingText"/> and
	/// <paramref name="closingText"/> is <see langword="null"/>.
	/// </exception>
	public TextAutoClosingAction(TextAutoClosingActionKind kind, string? closingText)
	{
		if (kind != TextAutoClosingActionKind.None && closingText is null)
		{
			throw new ArgumentException(
				"An insert or skip action requires the closing text to insert or skip.",
				nameof(closingText));
		}

		Kind = kind;
		ClosingText = closingText;
	}

	/// <summary>
	/// Gets the kind of auto-closing action.
	/// </summary>
	public TextAutoClosingActionKind Kind { get; }

	/// <summary>
	/// Gets the text to insert or skip, or <see langword="null"/> for the default value.
	/// </summary>
	public string? ClosingText { get; }

	/// <summary>
	/// Creates an insert action for the specified closing text.
	/// </summary>
	/// <param name="closingText">The closing text to insert.</param>
	/// <returns>The insert action.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="closingText"/> is <see langword="null"/>.</exception>
	public static TextAutoClosingAction CreateInsert(string closingText)
	{
		ArgumentNullException.ThrowIfNull(closingText);
		return new(TextAutoClosingActionKind.InsertClosingText, closingText);
	}

	/// <summary>
	/// Creates a skip action for the specified closing text.
	/// </summary>
	/// <param name="closingText">The closing text to skip.</param>
	/// <returns>The skip action.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="closingText"/> is <see langword="null"/>.</exception>
	public static TextAutoClosingAction CreateSkip(string closingText)
	{
		ArgumentNullException.ThrowIfNull(closingText);
		return new(TextAutoClosingActionKind.SkipExistingClosingText, closingText);
	}
}
