namespace Nickelony.IDEKit.LanguageFeatures.Controllers.Completion;

/// <summary>
/// Describes the content the completion tooltip currently shows: the selected item's documentation exactly as the producer supplied it.
/// </summary>
/// <param name="Content">The tooltip content, or <see langword="null"/> when the tooltip shows nothing.</param>
/// <remarks>
/// The payload is host-owned and opaque: a string is stored as a string and any other payload is stored unchanged
/// for the presenter to render. Equality compares the wrapped payload with <see cref="object.Equals(object, object)"/> - structural for a string,
/// otherwise the payload's own equality, which is reference equality unless the payload overrides <see cref="object.Equals(object)"/>;
/// two states that render identically can compare unequal.
/// </remarks>
public readonly record struct CompletionTooltipContent(object? Content);
