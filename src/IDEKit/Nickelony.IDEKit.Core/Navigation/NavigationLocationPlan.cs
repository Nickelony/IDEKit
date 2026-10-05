using Nickelony.IDEKit.Core.Text;

namespace Nickelony.IDEKit.Core.Navigation;

/// <summary>The selection, caret and line to reveal that an editor applies for a <see cref="NavigationLocation"/>.</summary>
/// <param name="Selection">The selection to apply; an empty range is a caret without a selection.</param>
/// <param name="CaretOffset">The caret offset to apply, always inside or at an edge of <paramref name="Selection"/>.</param>
/// <param name="ScrollLine">
/// The one-based line to reveal, or <see langword="null"/> to reveal the line that contains the start of <paramref name="Selection"/>.
/// </param>
public readonly record struct NavigationLocationPlan(TextRange Selection, int CaretOffset, int? ScrollLine);
