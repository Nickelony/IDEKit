using System.Text.RegularExpressions;
using Nickelony.IDEKit.Core.Text;
using Nickelony.IDEKit.LanguageFeatures.SemanticTokens;

namespace Nickelony.Samples;

/// <summary>Finds the names of called functions in a document and publishes them as semantic tokens.</summary>
internal sealed partial class FunctionSemanticTokensSource : ISemanticTokensSource
{
	private readonly Dictionary<string, IReadOnlyList<SemanticToken>> _tokensByDocument = new(StringComparer.Ordinal);

	public event EventHandler<SemanticTokensUpdatedEventArgs>? SemanticTokensUpdated;

	public IReadOnlyList<SemanticToken> GetSemanticTokens(string documentId) =>
		_tokensByDocument.TryGetValue(documentId, out IReadOnlyList<SemanticToken>? tokens) ? tokens : [];

	/// <summary>Finds the called functions of the text again and raises <see cref="SemanticTokensUpdated"/>.</summary>
	internal void Update(string documentId, string text)
	{
		List<SemanticToken> tokens = [];

		foreach (Match match in CallPattern().Matches(text))
			tokens.Add(new SemanticToken(new TextRange(match.Index, match.Length), SemanticTokenTypes.Function));

		_tokensByDocument[documentId] = tokens;
		SemanticTokensUpdated?.Invoke(this, new SemanticTokensUpdatedEventArgs(documentId, tokens));
	}

	[GeneratedRegex(@"\b(?!(?:if|while|for|function|return|and|or|not)\b)[A-Za-z_][A-Za-z0-9_.]*(?=\s*\()", RegexOptions.CultureInvariant)]
	private static partial Regex CallPattern();
}
