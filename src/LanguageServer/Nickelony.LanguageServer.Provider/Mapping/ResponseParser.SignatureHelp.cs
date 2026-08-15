using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

internal static partial class ResponseParser
{
	// The line separator passed to the plain-text normalizer that projects signature-help
	// documentation into the plain-text-only signature model.
	private const string DocumentationNewLine = "\n";

	/// <summary>
	/// Parses a signature-help payload from an LSP signature-help response.
	/// </summary>
	/// <param name="response">The signature help response payload, or <see langword="null"/> when unavailable.</param>
	/// <returns>
	/// The parsed signature help payload including every signature with a label, or
	/// <see langword="null"/> when no signature with a label is present.
	/// </returns>
	/// <remarks>
	/// LSP defaults an omitted, negative, or out-of-range <c>activeSignature</c> to the first signature; the
	/// index maps across unusable payload entries so a skipped entry does not shift the server's intended choice.
	/// </remarks>
	internal static TextSignatureHelp? ParseSignatureHelp(SignatureHelpResponse? response)
	{
		if (response?.Signatures is not { Count: > 0 } signaturePayloads)
			return null;

		// An element index outside the payload cannot select anything; LSP defines the first signature as
		// the fallback, not the nearest usable one.
		int activeSignatureElementIndex = response.ActiveSignature is { } activeSignatureValue
			&& activeSignatureValue >= 0
			&& activeSignatureValue < signaturePayloads.Count
				? activeSignatureValue
				: 0;

		int activeSignatureIndex = 0;

		var signatures = new List<TextSignatureInformation>(signaturePayloads.Count);

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

	/// <summary>
	/// Interprets an LSP <c>activeParameter</c> value into the active parameter selection model.
	/// </summary>
	/// <param name="activeParameterElement">The raw payload value.</param>
	/// <returns>The parsed active parameter selection.</returns>
	/// <remarks>
	/// An absent property maps to <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> so the next
	/// fallback level applies; an explicit <see langword="null"/> maps to the LSP 3.18
	/// <see cref="TextSignatureActiveParameterSelection.None"/> state; a non-negative integer that fits
	/// <see cref="int"/> is used verbatim. A negative number follows the pre-3.18 convention in which
	/// servers signaled "no active parameter" with <c>-1</c>, so it maps to
	/// <see cref="TextSignatureActiveParameterSelection.None"/> as well; any other number (fractional or out of
	/// range) maps to <see cref="TextSignatureActiveParameterSelection.NotSpecified"/> like an unknown value.
	/// </remarks>
	private static TextSignatureActiveParameterSelection GetActiveParameter(JsonElement activeParameterElement)
	{
		return activeParameterElement.ValueKind switch
		{
			JsonValueKind.Undefined => TextSignatureActiveParameterSelection.NotSpecified,
			JsonValueKind.Null => TextSignatureActiveParameterSelection.None,
			JsonValueKind.Number when activeParameterElement.TryGetInt32(out int index) =>
				index < 0 ? TextSignatureActiveParameterSelection.None : TextSignatureActiveParameterSelection.At(index),
			_ => TextSignatureActiveParameterSelection.NotSpecified
		};
	}

	/// <summary>
	/// Projects an LSP markup payload into the plain-text-only signature model: the payload is read
	/// through the parser's shared markup policy and then has its fence lines removed, while inline
	/// markup is preserved as text.
	/// </summary>
	/// <param name="element">The markup payload to flatten.</param>
	/// <returns>The plain-text documentation, or <see langword="null"/> when the payload is blank.</returns>
	private static string? ExtractMarkupText(JsonElement element)
		=> BacktickFenceTextNormalizer.NormalizeForPlainText(ParseMarkupContent(element).Text, DocumentationNewLine);

	private static TextSignatureInformation CreateSignatureInformation(SignatureHelpSignaturePayload signaturePayload, string label)
	{
		string? documentation = signaturePayload.Documentation is { } documentationElement
			? ExtractMarkupText(documentationElement)
			: null;

		var parameters = new List<TextSignatureParameterInfo>();

		if (signaturePayload.Parameters is { Count: > 0 } parameterPayloads)
		{
			for (int i = 0; i < parameterPayloads.Count; i++)
			{
				SignatureHelpParameterPayload parameterPayload = parameterPayloads[i];

				string? parameterLabel = parameterPayload.Label.ValueKind == JsonValueKind.String
					? parameterPayload.Label.GetString()
					: SignatureLabelParser.TryExtractParameterLabel(label, parameterPayload.Label, out string? extractedLabel)
						? extractedLabel
						: null;

				string? parameterDocumentation = parameterPayload.Documentation is { } parameterDocumentationElement
					? ExtractMarkupText(parameterDocumentationElement)
					: null;

				parameters.Add(new TextSignatureParameterInfo(parameterLabel ?? string.Empty, parameterDocumentation));
			}
		}

		return new(label, documentation, GetActiveParameter(signaturePayload.ActiveParameter), parameters);
	}
}
