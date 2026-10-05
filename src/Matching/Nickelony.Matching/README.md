# Nickelony.Matching

**Fuzzy matching and scoring of text** for any caller: a subsequence matcher with the scoring of the quick open of VS Code, and the word matcher of its command palette.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.Matching.svg)](https://www.nuget.org/packages/Nickelony.Matching)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package references nothing, so a host that ranks a list of names takes it without an editor, a quick pick or a UI toolkit.

## Getting started

```sh
dotnet add package Nickelony.Matching --prerelease
```

```csharp
using Nickelony.Matching;

var matcher = new FuzzyMatcher("fmtdoc");

int score = matcher.Score("Format Document");                   // positive; 0 means no match
bool matched = matcher.TryMatch("Format Document", null, out FuzzyMatch match);

foreach (MatchRange range in match.LabelRanges)                  // F, m, t and "Doc": draw them bold
    Console.WriteLine($"{range.Offset}, {range.Length}");

bool inWords = WordMatcher.TryMatch("tlc", "Toggle Line Comment", out IReadOnlyList<MatchRange> ranges);
```

## Remarks

- **A matcher is immutable.** It is safe to call from several threads, and `Score` allocates nothing for a candidate of ordinary length, so a host can score every candidate on every keystroke.
- **A match is a subsequence.** The characters of the query appear in the candidate in order, compared ordinally and without regard to case.
- **The matcher only scores.** It orders nothing, so a caller breaks a tie between equal scores itself.

The [matching guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/MatchingGuide.md) owns the scoring rules and the differences from the scorer of VS Code.

## License

MIT © 2026 Kewin Kupilas.
