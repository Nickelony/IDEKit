namespace Nickelony.IDEKit.IntelliSense.Completion;

/// <summary>
/// Describes an additional annotation attached to a completion item.
/// </summary>
/// <remarks>
/// <para>
/// The values mirror the LSP <c>CompletionItemTag</c> numbers, so a language provider that receives
/// a protocol tag maps it one-to-one. A provider that receives a protocol tag the library does not
/// define ignores it, so an unknown tag never fails an item.
/// </para>
/// <para>
/// Hosts decide how a tag is presented; the library stores the annotation only. A renderer for
/// <see cref="Deprecated"/> conventionally strikes the label through while keeping the item
/// selectable and committable.
/// </para>
/// </remarks>
public enum TextCompletionTag
{
	/// <summary>
	/// The item is deprecated.
	/// </summary>
	Deprecated = 1,
}
