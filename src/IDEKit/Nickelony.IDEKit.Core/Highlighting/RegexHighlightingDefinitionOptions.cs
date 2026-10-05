namespace Nickelony.IDEKit.Core.Highlighting;

/// <summary>Configures a <c>RegexHighlightingDefinition</c>.</summary>
/// <param name="CacheVersion">
/// A function whose return value, when it changes, rebuilds the main rule set; <see langword="null"/> builds the rule set once and never rebuilds it.
/// </param>
/// <remarks>
/// Because the record compares <see cref="CacheVersion"/> by delegate reference, two options that behave identically but carry
/// distinct delegates compare unequal, so compare the members the caller cares about instead of relying on record equality. The record carries
/// no engine dependency, so a host can build one without referencing a binding package, starting from <see cref="Default"/> and overriding
/// only the members it needs.
/// </remarks>
public sealed record RegexHighlightingDefinitionOptions(Func<int>? CacheVersion = null)
{
	public static RegexHighlightingDefinitionOptions Default { get; } = new();
}
