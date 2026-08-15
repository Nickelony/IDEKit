using System.Globalization;

namespace Nickelony.IDEKit.IntelliSense.Signatures;

/// <summary>
/// Identifies the active parameter selection of a signature-help payload or one of its signature
/// options: not specified, explicitly none, or a zero-based parameter index.
/// </summary>
/// <remarks>
/// <para>
/// The type mirrors the protocol's <c>activeParameter</c> states, which distinguish an absent value
/// (the level does not override the selection) from an explicit <see langword="null"/> (no parameter
/// is active), so a producer maps a protocol value without losing a state. Use
/// <see cref="NotSpecified"/>, the default value of the type, when the level does not override the
/// selection, <see cref="None"/> when the level explicitly reports no active parameter, and
/// <see cref="At(int)"/> for a parameter index.
/// </para>
/// <para>
/// An index is the zero-based position within the signature's parameters. A value at or beyond the
/// parameter count falls back to the first parameter when the payload resolves the active
/// parameter, which is the protocol default; a signature without parameters reports no active
/// parameter. Negative values are not part of the model: a producer that reports no active
/// parameter uses <see cref="None"/> instead of a negative sentinel.
/// </para>
/// </remarks>
public readonly record struct TextSignatureActiveParameterSelection
{
	private const byte NoneState = 1;
	private const byte IndexState = 2;

	private readonly int _index;
	private readonly byte _state;

	/// <summary>
	/// Gets the selection that does not override the active parameter, used when the protocol value
	/// is absent. This is the default value of the type.
	/// </summary>
	public static TextSignatureActiveParameterSelection NotSpecified => default;

	/// <summary>
	/// Gets the selection that explicitly reports no active parameter, for example an unmatched
	/// named argument.
	/// </summary>
	public static TextSignatureActiveParameterSelection None { get; } = new(NoneState, 0);

	private TextSignatureActiveParameterSelection(byte state, int index)
	{
		_state = state;
		_index = index;
	}

	/// <summary>
	/// Creates a selection for the supplied zero-based parameter index.
	/// </summary>
	/// <param name="index">The zero-based parameter index.</param>
	/// <returns>The selection for the supplied index.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
	public static TextSignatureActiveParameterSelection At(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		return new(IndexState, index);
	}

	/// <summary>
	/// Gets a value indicating whether this selection names a parameter index.
	/// </summary>
	public bool IsSpecified => _state == IndexState;

	/// <summary>
	/// Gets a value indicating whether this selection explicitly reports no active parameter.
	/// </summary>
	public bool IsNone => _state == NoneState;

	/// <summary>
	/// Gets the selected zero-based parameter index, or <see langword="null"/> when the selection is
	/// <see cref="NotSpecified"/> or <see cref="None"/>.
	/// </summary>
	public int? Index => _state == IndexState ? _index : null;

	/// <inheritdoc/>
	public override string ToString()
		=> _state switch
		{
			NoneState => "None",
			IndexState => _index.ToString(CultureInfo.InvariantCulture),
			_ => "NotSpecified"
		};
}
