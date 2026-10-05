namespace Nickelony.IDEKit.LanguageFeatures.DocumentSymbols;

/// <summary>Narrows an outline by the filter rule a <see cref="DocumentSymbolRequest"/> documents.</summary>
public static class DocumentSymbolFilter
{
	/// <summary>Keeps the symbols whose name contains the filter text, and the ancestors of such symbols.</summary>
	/// <param name="symbols">The symbols to narrow.</param>
	/// <param name="filterText">The filter text; a blank value keeps every symbol.</param>
	/// <returns>
	/// The supplied list when <paramref name="filterText"/> is blank; otherwise, a new list of the surviving symbols, each a copy that carries
	/// only its surviving children.
	/// </returns>
	/// <exception cref="ArgumentNullException"><paramref name="symbols"/> or <paramref name="filterText"/> is <see langword="null"/>.</exception>
	/// <remarks>
	/// A symbol matches when its <see cref="DocumentSymbol.Name"/> contains <paramref name="filterText"/> by ordinal comparison
	/// without regard to case. A symbol stays when it matches or when a descendant matches, and it keeps only the children that stay.
	/// </remarks>
	public static IReadOnlyList<DocumentSymbol> FilterByName(IReadOnlyList<DocumentSymbol> symbols, string filterText)
	{
		ArgumentNullException.ThrowIfNull(symbols);
		ArgumentNullException.ThrowIfNull(filterText);

		if (string.IsNullOrWhiteSpace(filterText))
			return symbols;

		var result = new List<DocumentSymbol>(symbols.Count);

		foreach (DocumentSymbol symbol in symbols)
		{
			if (FilterSymbol(symbol, filterText) is { } filtered)
				result.Add(filtered);
		}

		return result;
	}

	private static DocumentSymbol? FilterSymbol(DocumentSymbol symbol, string filterText)
	{
		var matchingChildren = new List<DocumentSymbol>(symbol.Children.Count);

		foreach (DocumentSymbol child in symbol.Children)
		{
			if (FilterSymbol(child, filterText) is { } filteredChild)
				matchingChildren.Add(filteredChild);
		}

		if (!symbol.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) && matchingChildren.Count == 0)
			return null;

		return new DocumentSymbol(name: symbol.Name, kind: symbol.Kind, range: symbol.Range, selectionRange: symbol.SelectionRange)
		{
			Detail = symbol.Detail,
			Data = symbol.Data,
			Children = matchingChildren,
		};
	}
}
