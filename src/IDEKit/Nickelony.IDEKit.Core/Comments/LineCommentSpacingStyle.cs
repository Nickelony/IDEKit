namespace Nickelony.IDEKit.Core.Comments;

/// <summary>Specifies how a commented line follows its line-comment delimiter.</summary>
/// <remarks>
/// The convention only affects commenting: uncommenting always removes the delimiter plus one following space when present, so the convention
/// does not change the round-trip.
/// </remarks>
public enum LineCommentSpacingStyle
{
	/// <summary>Follows the delimiter with a single space.</summary>
	SpaceAfterDelimiter = 0,

	/// <summary>Inserts the bare delimiter with no trailing space.</summary>
	DelimiterOnly = 1,
}
