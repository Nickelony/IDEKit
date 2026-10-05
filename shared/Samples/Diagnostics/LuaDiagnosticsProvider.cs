using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.Samples;

/// <summary>
/// Reports a call to a function that neither the library nor the document declares as an error, after the markers and the trailing whitespace.
/// </summary>
/// <remarks>
/// The provider composes <see cref="RegexDiagnosticsProvider"/>, so the settings file keeps the checks that apply to any text.
/// </remarks>
internal sealed partial class LuaDiagnosticsProvider : IDiagnosticsProvider
{
	private readonly RegexDiagnosticsProvider _textChecks = new();

	public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default)
	{
		List<Diagnostic> diagnostics = [.. await _textChecks.GetDiagnosticsAsync(request, cancellationToken)];
		string text = request.DocumentText;

		HashSet<string> declared = [.. DeclarationScanner.FindFunctions(text).Select(function => function.Name)];

		declared.UnionWith(DeclarationScanner.FindVariables(text).Select(variable => variable.Name));
		declared.UnionWith(DeclarationScanner.FindFunctions(text).SelectMany(function => function.Parameters).Select(parameter => parameter.Name));

		foreach (Match call in CallPattern().Matches(DeclarationScanner.StripCommentsAndStrings(text)))
		{
			// A field of a declared table, such as a module that require returned, holds a function the sample does not resolve.
			string owner = call.Value.Split('.')[0];

			if (
				declared.Contains(call.Value)
				|| declared.Contains(owner)
				|| SampleKeywords.Functions.ContainsKey(call.Value)
				|| SampleKeywords.Keywords.Contains(call.Value)
			)
			{
				continue;
			}

			diagnostics.Add(
				new Diagnostic(DiagnosticSeverity.Error, $"Unknown function '{call.Value}'", new TextRange(call.Index, call.Length))
				{
					Source = "sample",
					Code = "unknown-function",
				}
			);
		}

		return diagnostics;
	}

	// A method call through ':' or a field call through '.' on a value names a member, which the sample does not resolve.
	[GeneratedRegex(@"(?<![\w.:])[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?(?=\s*\()", RegexOptions.CultureInvariant)]
	private static partial Regex CallPattern();
}
