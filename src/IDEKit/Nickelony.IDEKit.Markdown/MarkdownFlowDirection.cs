namespace Nickelony.IDEKit.Markdown;

/// <summary>The direction in which rendered Markdown content flows.</summary>
/// <remarks>
/// The enum names the two directions every editor toolkit supports, so a theme carries no toolkit type; each binding converts the value
/// to its own flow direction.
/// </remarks>
public enum MarkdownFlowDirection
{
	/// <summary>Content flows from left to right.</summary>
	LeftToRight,

	/// <summary>Content flows from right to left.</summary>
	RightToLeft,
}
