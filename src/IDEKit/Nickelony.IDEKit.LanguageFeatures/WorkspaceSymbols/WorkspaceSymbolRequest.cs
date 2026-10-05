namespace Nickelony.IDEKit.LanguageFeatures.WorkspaceSymbols;

/// <summary>Describes a workspace-symbol request: the query that the provider matches symbols against.</summary>
public sealed record WorkspaceSymbolRequest
{
	/// <param name="query">The text to match symbol names against; the matching rule belongs to the provider, and an empty query is valid.</param>
	/// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
	public WorkspaceSymbolRequest(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		Query = query;
	}

	/// <summary>The text to match symbol names against.</summary>
	public string Query { get; }
}
