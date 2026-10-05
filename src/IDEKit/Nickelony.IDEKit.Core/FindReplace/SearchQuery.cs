using System.Text.RegularExpressions;

namespace Nickelony.IDEKit.Core.FindReplace;

/// <summary>Describes a regular-expression text search.</summary>
/// <param name="Pattern">
/// A <see langword="default"/> instance carries <see langword="null"/> here despite the annotation; the find helpers reject such a query
/// with <see cref="ArgumentNullException"/>.
/// </param>
/// <param name="Options">
/// The find and replace helpers define their own search direction, so they reject <see cref="RegexOptions.RightToLeft"/>
/// with an <see cref="ArgumentException"/>.
/// </param>
/// <param name="MatchTimeout">
/// The maximum time for a single match attempt, or <see cref="TimeSpan.Zero"/> (the default) to use <see cref="Regex.InfiniteMatchTimeout"/>;
/// passing <see cref="Regex.InfiniteMatchTimeout"/> itself means the same.
/// </param>
/// <remarks>
/// For patterns from an untrusted source, add <see cref="RegexOptions.NonBacktracking"/> so the engine rejects a pattern it cannot run
/// without backtracking instead of relying on the match timeout; construction then throws <see cref="NotSupportedException"/>.
/// </remarks>
public readonly record struct SearchQuery(string Pattern, RegexOptions Options, TimeSpan MatchTimeout = default);
