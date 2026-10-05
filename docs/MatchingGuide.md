# Matching guide

`Nickelony.Matching` scores text against a query for any list that filters as the user types. `FuzzyMatcher` is the
subsequence matcher of the quick open of VS Code, and `WordMatcher` is the word matcher of its command palette. The
package references nothing, and the [quick pick](QuickPickGuide.md) builds its filter modes on it.

## Score a list

A host constructs the matcher when the text changes and scores every candidate with it, because the query is
prepared once:

```csharp
using Nickelony.Matching;

var matcher = new FuzzyMatcher("fmtdoc");

int score = matcher.Score("Format Document");                   // positive; 0 means no match
bool matched = matcher.TryMatch("Format Document", null, out FuzzyMatch match);

foreach (MatchRange range in match.LabelRanges)                  // F, m, t and "Doc": draw them bold
    Highlight(range.Offset, range.Length);
```

- **A score of `0` means no match.** `Score` returns a positive number for a match, and the number has no unit, so
  it ranks candidates and is not a percentage. An empty query, or one of whitespace, matches nothing, and `IsEmpty`
  tells a host to show every candidate instead.
- **`Score` allocates nothing for a candidate of ordinary length.** `TryMatch` also builds the ranges to highlight,
  so a host calls it for the candidates it shows and `Score` for the rest.
- **A matcher is immutable and safe to call from several threads.**
- **The matcher only scores.** It orders nothing, so a caller breaks a tie between equal scores itself, as VS Code
  does: by the compactness of the match, the length of the label, the distance of the match, the length of the
  text and the label.

## What matches

- **A match is a subsequence.** The characters of the query appear in the candidate in order, compared ordinally
  and without regard to case.
- **Whitespace splits the query into pieces that must all match.** The scores add and the matched ranges merge. A
  piece in double quotes must match as one contiguous run, the characters `*`, `…` and `"` are dropped, and `/` and
  `\` are equal.
- **The label is the primary text and the description is the secondary text.** In `FuzzyMatchMode.Text` the
  description joins the label directly when the label alone does not match. In `FuzzyMatchMode.Path` the label is a
  file name and the description is its folder, and a query without a separator prefers the file name.
- **A candidate that lacks the query in order is rejected in one pass.** The matrix of a pair is built only for a
  candidate that holds the query as a subsequence, and a pair whose lengths multiply past a bound never matches.
  The bound is stated on the remarks of `FuzzyMatcher`, and a candidate of ordinary length stays far below it.

## What a score rewards

A match scores a point per matched character and earns a bonus for a character at the start of the candidate, a
character at a word start (after a space or one of `/ \ _ - . ' " :`), an upper-case letter that begins a run, a
character that continues a run, and a character whose case equals the query's, so `uie` finds `IUiDispatcher` and
`xhr` finds `XMLHttpRequest`. A label that starts with the query outranks any other label match, a label match
outranks a match that needs the description, and in `FuzzyMatchMode.Path` a query equal to the whole path scores
highest.

The scores follow the fuzzy scorer of VS Code, so a host that lists files or symbols ranks them the way that
editor does. The remarks of `FuzzyMatcher` name the differences: how a description joins the label, the trailing
`#` that the query keeps, the comparison of one UTF-16 unit at a time, and the bound on a pair.

## Match words

`WordMatcher.TryMatch` matches a query against the words of a text as the command palette of VS Code does, and
returns the matched characters as ranges. A match is the first of three forms that holds, all ignoring case:

1. The query is a prefix of the text.
2. Each character of the query matches the next character of a word, or the first character of a later word, where
   a word is a run that a space or a tab ends.
3. The query is a contiguous substring of the text.

The search for word starts backtracks, so a step budget bounds it. A text that exhausts the budget has no
word-start match, and the substring form can still match it.
