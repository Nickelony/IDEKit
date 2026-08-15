using Nickelony.IDEKit.IntelliSense.Signatures;
using System.Text.Json;

namespace Nickelony.LanguageServer.Provider;

public abstract partial class LanguageServerIntelliSenseProviderBase
{
	/// <inheritdoc/>
	public virtual Task<TextSignatureHelp?> GetSignatureHelpAsync(LanguageServerSignatureHelpRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		return SendDocumentPositionRequestAsync<SignatureHelpResponse?, TextSignatureHelp?>(
			request.FilePath, request.DocumentText, request.Position, LspMethodNames.SignatureHelp,
			(textDocument, position) => new SignatureHelpParams(textDocument, position, BuildSignatureHelpContext(request.Context)),
			ResponseParser.ParseSignatureHelp,
			fallbackValue: null,
			cancellationToken);
	}

	/// <summary>
	/// Projects an editor-side trigger context into the protocol payload, or reports
	/// <see langword="null"/> when the caller supplied no context.
	/// </summary>
	private static SignatureHelpContextPayload? BuildSignatureHelpContext(TextSignatureHelpContext? context)
	{
		if (context is null)
			return null;

		// The protocol values match the editor model's, so the trigger-kind mapping is a straight
		// numeric cast and an unknown value stays representable on the wire.
		return new SignatureHelpContextPayload(
			TriggerKind: (SignatureHelpTriggerKind)(int)context.TriggerKind,
			IsRetrigger: context.IsRetrigger,
			TriggerCharacter: context.TriggerCharacter,
			ActiveSignatureHelp: BuildActiveSignatureHelp(context.ActiveSignatureHelp));
	}

	/// <summary>
	/// Rebuilds the visible signature-help payload as a spec-shaped <c>SignatureHelp</c> object.
	/// </summary>
	/// <remarks>
	/// The editor model is a plain-text projection, so labels and documentation are forwarded as plain strings and
	/// the signature-level active parameter is left to the payload-level index, which carries the effective
	/// selection.
	/// </remarks>
	private static SignatureHelpResponse? BuildActiveSignatureHelp(TextSignatureHelp? activeSignatureHelp)
	{
		if (activeSignatureHelp is null)
			return null;

		IReadOnlyList<TextSignatureInformation> signatures = activeSignatureHelp.Signatures;
		var signaturePayloads = new SignatureHelpSignaturePayload[signatures.Count];

		for (int i = 0; i < signatures.Count; i++)
		{
			TextSignatureInformation signature = signatures[i];
			IReadOnlyList<TextSignatureParameterInfo> parameters = signature.Parameters;
			SignatureHelpParameterPayload[]? parameterPayloads = parameters.Count > 0
				? new SignatureHelpParameterPayload[parameters.Count]
				: null;

			if (parameterPayloads is not null)
			{
				for (int j = 0; j < parameters.Count; j++)
				{
					TextSignatureParameterInfo parameter = parameters[j];

					parameterPayloads[j] = new SignatureHelpParameterPayload
					{
						Label = JsonSerializer.SerializeToElement(parameter.Label),
						Documentation = parameter.Documentation is { } parameterDocumentation
							? JsonSerializer.SerializeToElement(parameterDocumentation)
							: null
					};
				}
			}

			signaturePayloads[i] = new SignatureHelpSignaturePayload
			{
				Label = signature.Label,
				Documentation = signature.Documentation is { } signatureDocumentation
					? JsonSerializer.SerializeToElement(signatureDocumentation)
					: null,
				Parameters = parameterPayloads
			};
		}

		return new SignatureHelpResponse
		{
			ActiveSignature = activeSignatureHelp.ActiveSignatureIndex,
			// A null effective index is written as an explicit JSON null: that is the LSP 3.18
			// "no active parameter" state, while an omitted property would tell the server to fall
			// back to the first parameter. A pre-3.18 schema that types the property as uinteger
			// would treat the explicit null as absent.
			ActiveParameter = activeSignatureHelp.ActiveParameterIndex is int activeParameter
				? JsonSerializer.SerializeToElement(activeParameter)
				: JsonSerializer.SerializeToElement<int?>(null),
			Signatures = signaturePayloads
		};
	}
}
