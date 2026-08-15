using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Nickelony.LanguageServer.Client;

/// <summary>
/// Extracts parameter labels from LSP signature labels using the protocol's label-offset form.
/// </summary>
public static class SignatureLabelParser
{
	/// <summary>
	/// Extracts the parameter label substring from a signature label using the protocol's
	/// <c>[start, end]</c> offset form, clamping the offsets to the label bounds.
	/// </summary>
	/// <param name="signatureLabel">The parent signature label.</param>
	/// <param name="parameterLabelElement">The parameter label JSON element; only the two-element offset-array form is handled here.</param>
	/// <param name="parameterLabel">Receives the extracted parameter label.</param>
	/// <returns><see langword="true"/> when the element carried an exact two-number offset pair that selects at least one character; otherwise, <see langword="false"/>.</returns>
	public static bool TryExtractParameterLabel(string signatureLabel, JsonElement parameterLabelElement,
		[NotNullWhen(true)] out string? parameterLabel)
	{
		parameterLabel = null;

		if (string.IsNullOrEmpty(signatureLabel) || parameterLabelElement.ValueKind != JsonValueKind.Array)
			return false;

		JsonElement.ArrayEnumerator labelParts = parameterLabelElement.EnumerateArray();

		if (!labelParts.MoveNext() || labelParts.Current.ValueKind != JsonValueKind.Number || !labelParts.Current.TryGetInt32(out int startIndex))
			return false;

		if (!labelParts.MoveNext() || labelParts.Current.ValueKind != JsonValueKind.Number || !labelParts.Current.TryGetInt32(out int endIndex))
			return false;

		// The protocol defines the offset form as exactly two numbers; extra elements mean the payload is not
		// the shape this parser handles, so it fails closed instead of guessing.
		if (labelParts.MoveNext())
			return false;

		startIndex = Math.Max(0, Math.Min(startIndex, signatureLabel.Length));
		endIndex = Math.Max(startIndex, Math.Min(endIndex, signatureLabel.Length));

		if (endIndex <= startIndex)
			return false;

		parameterLabel = signatureLabel[startIndex..endIndex];
		return true;
	}
}
