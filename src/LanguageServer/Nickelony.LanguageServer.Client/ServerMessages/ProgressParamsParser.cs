using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Nickelony.LanguageServer.Client.Protocol;

namespace Nickelony.LanguageServer.Client;

/// <summary>Parses the parameters of a <c>$/progress</c> notification that carry a work-done progress report.</summary>
internal static class ProgressParamsParser
{
	/// <summary>Parses one notification's parameters.</summary>
	/// <param name="element">The notification parameters.</param>
	/// <param name="progress">Receives the progress when the parameters carry a usable work-done report.</param>
	/// <returns>
	/// <see langword="true"/> when the token is a string or an integer and the value is a <c>begin</c>,
	/// <c>report</c> or <c>end</c> report; otherwise, <see langword="false"/>, for example for a partial-result value.
	/// </returns>
	internal static bool TryParse(JsonElement element, [NotNullWhen(true)] out ProgressParams? progress)
	{
		progress = null;

		if (!JsonElementReader.TryGetProperty(element, "token", out JsonElement tokenElement))
			return false;

		string? token = tokenElement.ValueKind switch
		{
			JsonValueKind.String => tokenElement.GetString(),
			JsonValueKind.Number when tokenElement.TryGetInt64(out long number) => number.ToString(CultureInfo.InvariantCulture),
			_ => null,
		};

		if (string.IsNullOrEmpty(token) || !JsonElementReader.TryGetProperty(element, "value", out JsonElement value))
			return false;

		string? kind = JsonElementReader.GetString(value, "kind");

		if (kind is not ("begin" or "report" or "end"))
			return false;

		int? percentage =
			JsonElementReader.TryGetProperty(value, "percentage", out JsonElement percentageElement)
			&& percentageElement.ValueKind == JsonValueKind.Number
			&& percentageElement.TryGetInt32(out int percentageValue)
				? percentageValue
				: null;

		progress = new ProgressParams(
			token,

			new WorkDoneProgressPayload(
				kind,
				Title: JsonElementReader.GetString(value, "title"),
				Message: JsonElementReader.GetString(value, "message"),
				percentage
			)
		);

		return true;
	}
}
