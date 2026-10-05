using System.Text.Json.Serialization;

namespace Nickelony.LanguageServer.Client.Protocol;

/// <summary>Represents the <c>label</c> of a signature-help parameter: the label text, or its offsets inside the signature's label.</summary>
/// <remarks>
/// <para>
/// The protocol's <c>string | [uinteger, uinteger]</c> union has two forms. <see cref="Text"/> carries the first,
/// and <see cref="StartOffset"/> with <see cref="EndOffset"/> carry the second, which selects <c>[start, end)</c> of the signature label
/// in UTF-16 code units. The offsets are the server's values as sent, so a consumer clamps them to the signature label.
/// </para>
/// <para>
/// The <see langword="default"/> value carries neither form. It is what a missing label, and a label
/// of any other shape, reads as, and it is not written.
/// </para>
/// </remarks>
[JsonConverter(typeof(SignatureHelpParameterLabelPayloadJsonConverter))]
public readonly record struct SignatureHelpParameterLabelPayload
{
	private SignatureHelpParameterLabelPayload(string? text, int? startOffset, int? endOffset)
	{
		Text = text;
		StartOffset = startOffset;
		EndOffset = endOffset;
	}

	/// <summary>The label text, or <see langword="null"/> when the label is not in its text form.</summary>
	public string? Text { get; }

	/// <summary>
	/// The inclusive start offset of the label inside the signature label, or <see langword="null"/> when the label is not in its offset form.
	/// </summary>
	public int? StartOffset { get; }

	/// <summary>
	/// The exclusive end offset of the label inside the signature label, or <see langword="null"/> when the label is not in its offset form.
	/// </summary>
	public int? EndOffset { get; }

	/// <summary>Creates the text form of a label.</summary>
	/// <param name="text">The label text.</param>
	/// <returns>The label.</returns>
	public static SignatureHelpParameterLabelPayload FromText(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		return new(text, startOffset: null, endOffset: null);
	}

	/// <summary>Creates the offset form of a label.</summary>
	/// <param name="startOffset">The inclusive start offset inside the signature label.</param>
	/// <param name="endOffset">The exclusive end offset inside the signature label.</param>
	/// <returns>The label.</returns>
	public static SignatureHelpParameterLabelPayload FromOffsets(int startOffset, int endOffset) => new(text: null, startOffset, endOffset);
}
