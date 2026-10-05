using System.Text.Json;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Provider;

/// <summary>
/// Projects the editor's signature-help trigger context onto the protocol payload that a <c>textDocument/signatureHelp</c> request carries.
/// </summary>
internal static class SignatureHelpContextConversion
{
	/// <summary>
	/// Projects an editor-side trigger context into the protocol payload, or reports <see langword="null"/> when the caller supplied no context.
	/// </summary>
	internal static SignatureHelpContextPayload? ToProtocol(SignatureHelpContext? context)
	{
		if (context is null)
			return null;

		return new SignatureHelpContextPayload(
			TriggerKind: SignatureHelpTriggerKindConversion.ToProtocol(context.TriggerKind),
			IsRetrigger: context.IsRetrigger,
			TriggerCharacter: context.TriggerCharacter,
			ActiveSignatureHelp: ToProtocolResponse(context.ActiveSignatureHelp)
		);
	}

	/// <summary>Rebuilds the visible signature-help payload as a spec-shaped <c>SignatureHelp</c> object.</summary>
	/// <remarks>
	/// The editor model is a plain-text projection, so labels are forwarded as label text and documentation as plain-text markup,
	/// and the signature-level active parameter is left to the payload-level index, which carries the effective selection.
	/// </remarks>
	private static SignatureHelpResponse? ToProtocolResponse(SignatureHelpInformation? activeSignatureHelp)
	{
		if (activeSignatureHelp is null)
			return null;

		IReadOnlyList<SignatureInformation> signatures = activeSignatureHelp.Signatures;
		var signaturePayloads = new SignatureInformationPayload[signatures.Count];

		for (int i = 0; i < signatures.Count; i++)
		{
			SignatureInformation signature = signatures[i];
			IReadOnlyList<ParameterInformation> parameters = signature.Parameters;
			ParameterInformationPayload[]? parameterPayloads = parameters.Count > 0 ? new ParameterInformationPayload[parameters.Count] : null;

			if (parameterPayloads is not null)
			{
				for (int j = 0; j < parameters.Count; j++)
				{
					ParameterInformation parameter = parameters[j];

					parameterPayloads[j] = new ParameterInformationPayload
					{
						Label = SignatureHelpParameterLabelPayload.FromText(parameter.Label),

						Documentation = parameter.Documentation is { } parameterDocumentation
							? new MarkupContentPayload
							{
								Kind = MarkupKindConversion.ToProtocol(parameterDocumentation.Kind),
								Value = parameterDocumentation.Value,
							}
							: null,
					};
				}
			}

			signaturePayloads[i] = new SignatureInformationPayload
			{
				Label = signature.Label,

				Documentation = signature.Documentation is { } signatureDocumentation
					? new MarkupContentPayload
					{
						Kind = MarkupKindConversion.ToProtocol(signatureDocumentation.Kind),
						Value = signatureDocumentation.Value,
					}
					: null,

				Parameters = parameterPayloads,
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

			Signatures = signaturePayloads,
		};
	}
}
