using System.Diagnostics.CodeAnalysis;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>Extracts parameter labels from LSP signature labels using the protocol's label-offset form.</summary>
internal static class ParameterLabelParser
{
	/// <summary>
	/// Extracts the parameter label substring and its range from a signature label using the protocol's <c>[start, end]</c> offset form,
	/// clamping the offsets to the label bounds.
	/// </summary>
	/// <param name="signatureLabel">The parent signature label.</param>
	/// <param name="parameterLabelPayload">The parameter label; only its offset form is handled here.</param>
	/// <param name="parameterLabel">Receives the extracted parameter label.</param>
	/// <param name="startIndex">Receives the clamped inclusive start offset of the label within <paramref name="signatureLabel"/>.</param>
	/// <param name="endIndex">Receives the clamped exclusive end offset of the label within <paramref name="signatureLabel"/>.</param>
	/// <returns>
	/// <see langword="true"/> when the label carried an offset pair that selects at least one character; otherwise, <see langword="false"/>.
	/// </returns>
	internal static bool TryParse(
		string signatureLabel,
		SignatureHelpParameterLabelPayload parameterLabelPayload,
		[NotNullWhen(true)] out string? parameterLabel,
		out int startIndex,
		out int endIndex
	)
	{
		parameterLabel = null;
		startIndex = 0;
		endIndex = 0;

		if (
			string.IsNullOrEmpty(signatureLabel)
			|| parameterLabelPayload.StartOffset is not { } rawStartIndex
			|| parameterLabelPayload.EndOffset is not { } rawEndIndex
		)
		{
			return false;
		}

		int clampedStartIndex = Math.Max(0, Math.Min(rawStartIndex, signatureLabel.Length));
		int clampedEndIndex = Math.Max(clampedStartIndex, Math.Min(rawEndIndex, signatureLabel.Length));

		if (clampedEndIndex <= clampedStartIndex)
			return false;

		startIndex = clampedStartIndex;
		endIndex = clampedEndIndex;
		parameterLabel = signatureLabel[clampedStartIndex..clampedEndIndex];
		return true;
	}
}
