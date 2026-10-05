using System.Text.RegularExpressions;
using Nickelony.IDEKit.LanguageFeatures.SignatureHelp;

namespace Nickelony.Samples;

/// <summary>Answers signature help inside the argument list of a call to one of the library functions.</summary>
internal sealed partial class FunctionSignatureHelpProvider : ISignatureHelpProvider, ISignatureHelpTriggerCharacters
{
	// The signature shows from the opening parenthesis of a call on, and a comma moves it to the next parameter.
	public IReadOnlyList<string> TriggerCharacters { get; } = ["(", ","];

	public IReadOnlyList<string> RetriggerCharacters { get; } = [];

	public Task<SignatureHelpInformation?> GetSignatureHelpAsync(SignatureHelpRequest request, CancellationToken cancellationToken = default)
	{
		string text = request.DocumentText;
		int caret = Math.Clamp(request.CaretOffset, 0, text.Length);

		int openParenthesis = text.LastIndexOf('(', Math.Max(caret - 1, 0), caret == 0 ? 0 : caret);

		if (openParenthesis < 0 || text.IndexOf(')', openParenthesis, caret - openParenthesis) >= 0)
			return Task.FromResult<SignatureHelpInformation?>(null);

		// A library function can be a dotted name, which the identifier policy of Core does not span.
		string callee = CalleePattern().Match(text[..openParenthesis]).Value;

		if (!SampleKeywords.Functions.TryGetValue(callee, out SampleFunction? function))
			return Task.FromResult<SignatureHelpInformation?>(null);

		int commas = text.AsSpan(openParenthesis + 1, caret - openParenthesis - 1).Count(',');

		List<ParameterInformation> parameters =
		[
			.. function.Parameters.Select(parameter =>
			{
				string[] parts = parameter.Split(": ", 2);
				return new ParameterInformation(parts[0], parts.Length > 1 ? parts[1] : null);
			}),
		];

		var signature = new SignatureInformation(
			label: $"{function.Name}({string.Join(", ", parameters.Select(parameter => parameter.Label))})",
			documentation: function.Summary,
			activeParameter: SignatureActiveParameterSelection.At(Math.Min(commas, parameters.Count - 1)),
			parameters
		);

		return Task.FromResult<SignatureHelpInformation?>(new SignatureHelpInformation([signature]));
	}

	[GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
	private static partial Regex CalleePattern();
}
