using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Diagnostics;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.Diagnostics;

namespace Nickelony.Samples;

/// <summary>Reports markers and trailing whitespace in a document, found with regular expressions.</summary>
internal sealed partial class RegexDiagnosticsProvider : IDiagnosticsProvider
{
	public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default)
	{
		List<Diagnostic> diagnostics = [];

		foreach (Match match in MarkerPattern().Matches(request.DocumentText))
		{
			diagnostics.Add(
				new Diagnostic(DiagnosticSeverity.Warning, $"{match.Value} marker", new TextRange(match.Index, match.Length))
				{
					Source = "sample",
					Code = "marker",
				}
			);
		}

		foreach (Match match in TrailingWhitespacePattern().Matches(request.DocumentText))
		{
			diagnostics.Add(
				new Diagnostic(DiagnosticSeverity.Information, "Trailing whitespace", new TextRange(match.Index, match.Length))
				{
					Source = "sample",
					Code = "trailing-whitespace",
				}
			);
		}

		return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
	}

	[GeneratedRegex(@"\b(TODO|FIXME)\b", RegexOptions.CultureInvariant)]
	private static partial Regex MarkerPattern();

	[GeneratedRegex(@"[ \t]+(?=\r?$)", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
	private static partial Regex TrailingWhitespacePattern();
}
