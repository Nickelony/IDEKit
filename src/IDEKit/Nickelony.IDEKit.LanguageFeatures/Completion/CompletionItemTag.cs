namespace Nickelony.IDEKit.LanguageFeatures.Completion;

/// <summary>Describes an additional annotation attached to a completion item.</summary>
/// <remarks>
/// The values mirror the LSP <c>CompletionItemTag</c> numbers, so a provider maps a protocol tag one-to-one and ignores one the library
/// does not define. Hosts decide presentation: a renderer for <see cref="Deprecated"/> conventionally strikes the label through while keeping
/// the item selectable and committable.
/// </remarks>
public enum CompletionItemTag
{
	Deprecated = 1,
}
