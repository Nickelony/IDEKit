using System.Text.Json;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Markup;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

internal static class SignatureHelpResponseParser
{
	/// <summary>Parses a signature-help payload from an LSP signature-help response.</summary>
	/// <param name="response">The signature help response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>
	/// The parsed signature help payload including every signature with a label, or <see langword="null"/> when no signature with a label is present.
	/// </returns>
	/// <remarks>
	/// LSP defaults an omitted, negative, or out-of-range <c>activeSignature</c> to the first signature; the index maps across unusable payload
	/// entries so a skipped entry does not shift the server's intended choice.
	/// </remarks>
	internal static SignatureHelpInformation? Parse(SignatureHelpResponse? response)
	{
		if (response?.Signatures is not { Count: > 0 } signaturePayloads)
			return null;

		// An element index outside the payload cannot select anything; LSP defines the first signature as
		// the fallback, not the nearest usable one.
		int activeSignatureElementIndex =
			response.ActiveSignature is { } activeSignatureValue && activeSignatureValue >= 0 && activeSignatureValue < signaturePayloads.Count
				? activeSignatureValue
				: 0;

		int activeSignatureIndex = 0;

		var signatures = new List<SignatureInformation>(signaturePayloads.Count);

		for (int i = 0; i < signaturePayloads.Count; i++)
		{
			if (signaturePayloads[i] is not { } signaturePayload || string.IsNullOrWhiteSpace(signaturePayload.Label))
				continue;

			// Track where the active signature lands once unusable entries are skipped.
			if (i < activeSignatureElementIndex)
				activeSignatureIndex++;

			signatures.Add(CreateSignatureInformation(signaturePayload, signaturePayload.Label));
		}

		if (signatures.Count == 0)
			return null;

		// The mapped index can land past the last usable entry when the active element or the trailing
		// elements were unusable; the last usable signature is then the closest valid choice.
		activeSignatureIndex = Math.Min(activeSignatureIndex, signatures.Count - 1);

		return new(signatures, activeSignatureIndex, GetActiveParameter(response.ActiveParameter));
	}

	/// <summary>Interprets an LSP <c>activeParameter</c> value into the active parameter selection model.</summary>
	/// <param name="activeParameterElement">The raw payload value.</param>
	/// <returns>The parsed active parameter selection.</returns>
	/// <remarks>
	/// An absent property maps to <see cref="SignatureActiveParameterSelection.NotSpecified"/> so the next
	/// fallback level applies; an explicit <see langword="null"/> maps to the LSP 3.18 <see cref="SignatureActiveParameterSelection.None"/> state;
	/// a non-negative integer that fits <see cref="int"/> is used verbatim. A negative number follows the pre-3.18 convention in which servers
	/// signaled "no active parameter" with <c>-1</c>, so it maps to <see cref="SignatureActiveParameterSelection.None"/> as well; any other number
	/// (fractional or out of range) maps to <see cref="SignatureActiveParameterSelection.NotSpecified"/> like an unknown value.
	/// </remarks>
	private static SignatureActiveParameterSelection GetActiveParameter(JsonElement activeParameterElement)
	{
		return activeParameterElement.ValueKind switch
		{
			JsonValueKind.Undefined => SignatureActiveParameterSelection.NotSpecified,
			JsonValueKind.Null => SignatureActiveParameterSelection.None,

			JsonValueKind.Number when activeParameterElement.TryGetInt32(out int index) => index < 0
				? SignatureActiveParameterSelection.None
				: SignatureActiveParameterSelection.At(index),

			_ => SignatureActiveParameterSelection.NotSpecified,
		};
	}

	private static SignatureInformation CreateSignatureInformation(SignatureInformationPayload signaturePayload, string label)
	{
		MarkupContent? documentation = MarkupContentParser.Parse(signaturePayload.Documentation);

		var parameters = new List<ParameterInformation>();

		if (signaturePayload.Parameters is { Count: > 0 } parameterPayloads)
		{
			for (int i = 0; i < parameterPayloads.Count; i++)
			{
				ParameterInformationPayload parameterPayload = parameterPayloads[i];

				string? parameterLabel;
				TextRange? parameterRange = null;

				if (parameterPayload.Label.Text is { } labelText)
				{
					parameterLabel = labelText;
				}
				else if (
					ParameterLabelParser.TryParse(
						signatureLabel: label,
						parameterLabelPayload: parameterPayload.Label,
						parameterLabel: out string? extractedLabel,
						out int startIndex,
						out int endIndex
					)
				)
				{
					// The offset form carries the exact position of the parameter within the signature label, so the
					// payload can highlight it unambiguously instead of searching for a label that may repeat.
					parameterLabel = extractedLabel;
					parameterRange = TextRange.FromBounds(startIndex, endIndex);
				}
				else
				{
					parameterLabel = null;
				}

				MarkupContent? parameterDocumentation = MarkupContentParser.Parse(parameterPayload.Documentation);

				parameters.Add(new ParameterInformation(parameterLabel ?? string.Empty, parameterDocumentation, parameterRange));
			}
		}

		return new(label, documentation, activeParameter: GetActiveParameter(signaturePayload.ActiveParameter), parameters);
	}
}
