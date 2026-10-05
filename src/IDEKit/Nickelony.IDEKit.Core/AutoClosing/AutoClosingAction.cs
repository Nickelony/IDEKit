namespace Nickelony.IDEKit.Core.AutoClosing;

/// <summary>Describes an auto-closing action.</summary>
/// <remarks>
/// A default-initialized value has <see cref="AutoClosingActionKind.None"/> and a <see langword="null"/> <see cref="ClosingText"/>
/// and applies nothing; an insert or skip action without closing text cannot be constructed.
/// </remarks>
public readonly record struct AutoClosingAction
{
	/// <exception cref="ArgumentException">
	/// <paramref name="kind"/> is <see cref="AutoClosingActionKind.InsertClosingText"/>
	/// or <see cref="AutoClosingActionKind.SkipExistingClosingText"/> and <paramref name="closingText"/> is <see langword="null"/>.
	/// </exception>
	public AutoClosingAction(AutoClosingActionKind kind, string? closingText)
	{
		if (kind != AutoClosingActionKind.None && closingText is null)
			throw new ArgumentException("An insert or skip action requires the closing text to insert or skip.", nameof(closingText));

		Kind = kind;
		ClosingText = closingText;
	}

	public AutoClosingActionKind Kind { get; }

	/// <summary>The text to insert or skip, or <see langword="null"/> for the default value.</summary>
	public string? ClosingText { get; }

	/// <summary>Creates an insert action for the specified closing text.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="closingText"/> is <see langword="null"/>.</exception>
	public static AutoClosingAction CreateInsert(string closingText)
	{
		ArgumentNullException.ThrowIfNull(closingText);
		return new(AutoClosingActionKind.InsertClosingText, closingText);
	}

	/// <summary>Creates a skip action for the specified closing text.</summary>
	/// <exception cref="ArgumentNullException"><paramref name="closingText"/> is <see langword="null"/>.</exception>
	public static AutoClosingAction CreateSkip(string closingText)
	{
		ArgumentNullException.ThrowIfNull(closingText);
		return new(AutoClosingActionKind.SkipExistingClosingText, closingText);
	}
}
