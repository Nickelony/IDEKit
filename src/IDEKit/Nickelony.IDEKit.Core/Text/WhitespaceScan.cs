namespace Nickelony.IDEKit.Core.Text;

/// <summary>Owns the space-and-tab predicate the indentation and trailing-whitespace primitives use.</summary>
/// <remarks>
/// Those primitives recognize only spaces and tabs, so unusual Unicode whitespace is never consumed or removed silently;
/// every other primitive follows <see cref="char.IsWhiteSpace(char)"/>, so any Unicode whitespace counts. Do not apply both rules to one operation.
/// </remarks>
internal static class WhitespaceScan
{
	internal static bool IsSpaceOrTab(char character) => character is ' ' or '\t';
}
