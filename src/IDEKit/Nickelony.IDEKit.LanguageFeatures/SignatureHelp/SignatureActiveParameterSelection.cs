using System.Globalization;

namespace Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

/// <summary>
/// Identifies the active parameter selection of a signature-help payload
/// or one of its signature options: not specified, explicitly none, or a zero-based parameter index.
/// </summary>
/// <remarks>
/// The value mirrors the protocol's <c>activeParameter</c> states: <see cref="NotSpecified"/> (the default) on a signature defers to the payload's
/// selection and on the payload selects the first parameter; <see cref="None"/> reports no active parameter, and <see cref="At(int)"/> names
/// a zero-based index. An index at or beyond the parameter count falls back to the first parameter; a producer reports no active
/// parameter with <see cref="None"/>, never a negative index.
/// </remarks>
public readonly record struct SignatureActiveParameterSelection
{
	private const byte NoneState = 1;
	private const byte IndexState = 2;

	private readonly int _index;
	private readonly byte _state;

	/// <summary>
	/// The selection that does not override the active parameter; the default value of the type, used when the protocol value is absent.
	/// </summary>
	public static SignatureActiveParameterSelection NotSpecified => default;

	/// <summary>The selection that explicitly reports no active parameter, for example an unmatched named argument.</summary>
	public static SignatureActiveParameterSelection None { get; } = new(NoneState, 0);

	private SignatureActiveParameterSelection(byte state, int index)
	{
		_state = state;
		_index = index;
	}

	/// <summary>Creates a selection for the supplied zero-based parameter index.</summary>
	/// <param name="index">The zero-based parameter index.</param>
	/// <returns>The selection for the supplied index.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
	public static SignatureActiveParameterSelection At(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);
		return new(IndexState, index);
	}

	public bool IsSpecified => _state == IndexState;

	public bool IsNone => _state == NoneState;

	/// <summary>
	/// The selected zero-based parameter index, or <see langword="null"/> when the selection is <see cref="NotSpecified"/> or <see cref="None"/>.
	/// </summary>
	public int? Index => _state == IndexState ? _index : null;

	/// <inheritdoc/>
	public override string ToString()
	{
		return _state switch
		{
			NoneState => "None",
			IndexState => _index.ToString(CultureInfo.InvariantCulture),
			_ => "NotSpecified",
		};
	}
}
