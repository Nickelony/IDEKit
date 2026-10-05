# Repository Instructions

## Non-negotiables

- NEVER run `./format.ps1`, with or without a switch. It takes a very long time, and formatting is the owner's
  job: the owner runs it by hand. Write the words and leave the layout alone, and say in the report that
  formatting was not run.
- Keep style-only edits out of files the change does not otherwise touch: they inflate the review diff and
  hide the behavior change.
- Update the [public API baseline](#public-api-baseline) in the change that alters the public surface, or the
  build fails.
- A change that makes a doc wrong updates that doc in the same change.
- Do not record history in the tree: no changelog, no completed-work log, no "previously" or "used to" in a
  comment, and no plan, review, or wave id. See [Living documentation](#living-documentation).
- Build and test before reporting done, and separate failures the change caused from ones that were already
  there.

## Owner directive: clean before publish

- The solution must be fully clean before any package publication; publication is held until then.
- Breaking API changes are allowed and encouraged when they follow standards and make the code clearer.
  Delete a legacy member rather than marking it `[Obsolete]`. Delete compatibility shims, forwarding accessors
  and public surface that nothing produces, rather than keeping them for compatibility. Choose the cleaner
  option over a conservative fallback.
- Consumer projects (for example Tomb Editor) are not a design constraint; they are updated after the
  library settles.

## Layout

- `src/IDEKit/` - the editor-intelligence packages (`Nickelony.IDEKit.*`), including the WPF (`AvalonEdit/`)
  and cross-platform (`AvaloniaEdit/`) editor bindings.
- `src/LanguageServer/` - the language-server packages (`Nickelony.LanguageServer.*`).
- `src/KeyBindings/` - the key-binding packages (`Nickelony.KeyBindings.*`).
- `src/QuickPick/` - the quick-pick packages (`Nickelony.QuickPick.*`), which a host without an editor can take.
- `src/Matching/` - the fuzzy matcher (`Nickelony.Matching`), which references nothing.
- `samples/` - the runnable sample hosts, one per editor engine, over the source they share in `shared/Samples/`. A sample
  is neither packed nor referenced by a library.
- `shared/` - product source that more than one project compiles. `docs/` - the guides, published as a DocFX
  site (`docs/docfx.json`). `tests/` - the test projects, and the test source that several of them compile, in
  its family's folder: helpers in a `TestSupport/` folder and the shared test trees in a `Shared/` folder, such as
  `tests/IDEKit/Shared/`.

## Architecture

`docs/ArchitectureGuide.md` owns the tier, slice, namespace, shared-primitive, one-home and internals rules and the
reason for each, and the `Repository` guard tests check the ones a test can read from the repository's files. Read
it before adding a project, a project reference, a namespace, a `shared/` folder or an `InternalsVisibleTo` grant.

## Build and verify

```powershell
dotnet build .\Nickelony.IDEKit.slnx
dotnet test .\Nickelony.IDEKit.slnx
```

- A focused change may use a targeted project or test command when it gives equivalent coverage. A
  docs-only change runs the guards that read the repository's files:
  `dotnet test .\Nickelony.IDEKit.slnx --filter TestCategory=Repository`.
- Some tests render real WPF content in a window and need an interactive desktop; on a headless session
  `WpfTestHost` reports them inconclusive by design (`tests/TestSupport/README.md`).
- `.github/workflows/build.yml` builds the solution and runs its tests on every push and pull request. It deselects the
  environment-dependent test categories that `tests/TestSupport/README.md` lists, and the Stanza analyzer rules do not run there.
- Formatting is not part of the build. `dotnet build` runs the compiler, the .NET analyzers and the Stanza ZA
  rules, whose severities `.editorconfig` sets. It does not run `format.ps1`. It runs the IDE style and naming
  rules, such as IDE1006, only when `EnforceCodeStyleInBuild` is set, and this tree does not set it.
  `format.ps1` applies the fixes those rules offer: it builds `stanza-format` and `stanza-markup` from the
  Stanza checkout and runs them, and `-Check` reports what a run would change without writing.
- Markdown, JSON and `.editorconfig` are hand-kept; no formatter rewrites them.
- The documentation site is built from `docs/` by DocFX: run `dotnet tool restore`, then
  `dotnet docfx docs/docfx.json --serve` to preview it locally. `.github/workflows/docs.yml` publishes it to
  GitHub Pages.

## Public API baseline

Every library project is checked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`: list a new, changed, or
removed public member in `PublicAPI.Unshipped.txt` in the same change, or the build fails (RS0016/RS0017).
Nothing has shipped, so `PublicAPI.Shipped.txt` holds only its `#nullable enable` header.

To regenerate a baseline:

1. Cut both files back to their `#nullable enable` line. Without it, the lines lose their nullability
   annotations and RS0037 fires instead.
2. Build once with `-p:WarningsAsErrors=`.
3. Collect the declared-API line from each RS0016 message into `PublicAPI.Unshipped.txt`, one per line.

At a release, move the entries in each `PublicAPI.Unshipped.txt` into its `PublicAPI.Shipped.txt`, and leave
the unshipped file with only its `#nullable enable` header. After publication, a change to a shipped member follows
the evolution rules of `docs/ArchitectureGuide.md`, which say which changes are additive and which are breaking.

## Editor bindings

The same editor feature exists in every editor binding, so keep it single-source:

- Engine-free logic lives in `Nickelony.IDEKit.Core` / `Nickelony.IDEKit.LanguageFeatures`, or - when it needs
  a dependency `Core` must not take - in a Core-adjacent neutral package such as `Nickelony.IDEKit.Markdown`.
- A file that is identical modulo the engine namespace is shared, not copied: one body under `shared/Editor/`
  (or `shared/Editor.LanguageFeatures/`, `shared/Editor.Markdown/`, `shared/Editor.TextMate/`) is linked into
  each binding, with an `#if AVALONIAEDIT` header selecting the engine `using` and the file-scoped namespace.
  A binding project and its test project state their engine once, as `<EditorEngine>Wpf</EditorEngine>` or
  `<EditorEngine>Avalonia</EditorEngine>`, and `Directory.Build.targets` defines `AVALONIAEDIT` for Avalonia, so
  no project defines it by hand. The Avalonia key-binding adapter defines `AVALONIAKEYBINDINGS`, which selects the
  namespace of `shared/KeyBindings` the same way, and the Avalonia quick-pick package defines `AVALONIAQUICKPICK`
  for `shared/QuickPick`. `Nickelony.QuickPick` defines `QUICKPICKFAMILY`, which compiles `shared/Primitives` as
  internal types of that family, because the family references no Core package.
- Where the presentation layer genuinely differs (Avalonia has no `FlowDocument`), share the engine-neutral
  half behind an internal model and let each binding keep a thin emitter.
- Documentation that repeats across bindings is shared from one home: `<inheritdoc/>` from a
  `Core`/`LanguageFeatures` contract, or a fragment from `shared/docs/` when no contract exists. Never `cref`
  from one binding into another.
- A file of an engine binding or adapter never qualifies a name from the engine's root namespace
  (`AvaloniaEdit.TextEditor`), because the product namespace shadows it. It imports the engine namespace, or
  aliases one type with a neutral alias.

`docs/EditorSharingGuide.md` is the guide for this, and `docs/EditorBindingGuide.md` sections 1, 3, and 4 give
the binding shape.

## Code conventions

[`CONVENTIONS.md`](CONVENTIONS.md) holds the code rules that no compiler or formatter enforces: the names of
types, members and parameters, the namespaces, the contracts, the composition, the logging and the test shape.
Each rule has an id and says whether a guard test or a review keeps it. Read it before naming a type, a member or a
namespace. A review cites its rule ids and reports as [`REVIEWING.md`](REVIEWING.md) says: a verdict per
category, backed by the rules it cites.

## Codebase graph

When the codebase-memory MCP server is connected, query it before searching files: `list_projects` first,
`get_architecture` for the shape, `search_graph` or `trace_path` for symbols and callers, and
`get_code_snippet` to read a function. Read the file itself for exact content.

## Documentation

A doc exists so a reader can use the code now. These principles settle any case the rules below miss:

- **Accurate first.** Check each claim against the code or a test before writing it. A wordy doc is cheap to
  fix; a wrong one is not.
- **State the fact the signature does not carry, then stop.** A code example earns its place where a paragraph
  does not.
- **Plain, complete sentences.** Cut every word a sentence survives without, but keep its subject, its verb,
  and the word that carries the reasoning, such as `because`, `so` or `unless`. A fragment saves a word and
  costs the meaning.
- **Cut words, never facts.** Never cut a qualifier, a unit, a bound or a failure mode.
- **No backstory and no walls of text.** A reader who meets either pastes the file into a summarizer instead of
  reading it.

### What counts as a doc

A doc is any prose this repository owns: every Markdown file, an XML doc comment, an inline comment, a comment
in a project, config, script or workflow file, and an external member doc under `shared/docs/`. Read that as
kinds of file, so a new file type is in scope without an edit here. The guard lists the files it reads, so a new
kind needs an edit to the guard.

Not a doc: generated output (`bin/`, `obj/`, `artifacts/`, `.vs/`, `.codebase-memory/`, and the DocFX output
under `docs/api/` and `docs/_site/`), gitignored scratch (`temp/`), a machine-owned manifest such as
`PublicAPI.*.txt`, anything vendored such as the GitHub `.gitignore` template, and a JSON value, since strict
JSON has no comments.

### Rules

1. **Lead with one sentence.** A `<summary>` is one sentence, and a page's opening sentence says what its
   subject does. A comment's second sentence states an invariant the caller needs, and it goes in `<remarks>`.
2. **Only the non-obvious.** If the name and signature already say it, delete the doc; delete beats reword.
   Never restate a type or parameter name, "gets or sets", or framework behavior.
3. **No history or process.** State what the code does and what it guards against, never how it got there: no
   "previously", "no longer", "legacy", "migrated" or "used to", and no review, plan, wave, phase or test id.
4. **No filler or narration.** `DocumentationGuardTests` bans the padding, history and census phrases it lists,
   such as "simply", "just", "note that" and "in order to". A comment never addresses the reader; a page may
   say "you" where the imperative does not fit.
5. **No walls of text.** A run that outgrows its [aim](#documentation-shape) by a wide margin is split. A comment
   passage that needs a heading, a list or a second paragraph belongs on a page.
6. **No census of a set that grows.** Never count one ("the three rules", "both packages") or list its members
   outside the home a guard keeps in sync; name the family and link the home. A fixed pair, such as the two
   tags of an element, is not a census.
7. **Say it once.** A fact has one home: the code, the guide, or the readme that owns it. Every other mention
   states the consequence where it applies and links the home.
8. **No drift-prone literals.** A version, count, default or id range is stated once, on the member, constant
   or page that owns it. Code comments and other pages link that home rather than repeat the value. An
   illustrative key, id or value is labeled as an example.
9. **Concrete over generic.** Name the constraint, edge case, ordering rule or failure mode. A sentence that
   could describe any class is deleted.
10. **No cross-part narration.** Do not walk the reader through a neighbor type or another page's topic; mention
    it only where the text is unreadable without it.
11. **One term per concept.** Use the plain word, and the same word everywhere. A term this repository coins or
    narrows is defined once, in the guide that owns the concept, and linked where a page first uses it.
12. **Examples are real.** A Before/After pair shows what the fix produces, a comparison labels which side to
    follow, and a measurement says what it counts. Prefer an example a test already pins.

`docs/DocStyleGuide.md` shows the rules applied as before/after pairs.

### Code comments

- The Stanza rules `.editorconfig` enables lay out `///` comments, from the wrap width to the element order and
  the closing period. The owner's formatting pass applies them, so write the words and leave the layout to it.
- Mark a language keyword with `<see langword="..."/>`. `ZA0211` does it for `null`; the others are written by
  hand. A bare `null` stays only where it is a wire term.
- Documentation elements follow the order `ZA0212` enforces.
- An `<exception>` names the condition as a fragment, never with "Thrown when".
- A `cancellationToken` parameter reads "A token that can cancel the {operation}." An event reads
  "Raised when/after", and a protected hook the base invokes reads "Invoked when/after".
- A fallback clause after a semicolon takes a comma: `otherwise, <see .../>`.
- `CS1591` is not enforced, so a public member needs a doc only when it carries a fact its name and signature
  do not.
- A test needs no XML doc; its name states what it checks. A fixture or guard a reader cannot infer from its
  name earns one line.
- When a fact outgrows `<remarks>`, it moves to the guide that owns it, and the member keeps a one-line
  consequence and a link.

### Pages

- A page under `docs/` owns one topic and reads in one sitting. It leads with what the subject does, then a
  command or an example, then the detail.
- A guide names no other file unless the reader cannot follow the topic without it, and carries no history:
  no wave ids, no plan, no "how this came to be".

Pick the form by how the facts relate:

| The facts | The form |
| --- | --- |
| Stand alone and read in any order | A bulleted list |
| Happen in order | A numbered list |
| Share the same fields, such as a value and its effect | A table |
| Depend on each other through `because`, `so` or `unless` | A short paragraph |
| Describe behavior a reader could misread | A Before/After example |

- A bullet holds one fact as a complete sentence with a verb.
- The bullets of one list share a grammatical shape, and either every bullet opens with a bold lead-in or none
  does.
- A list nests one level at most and never holds a single item.
- Instructions use the imperative. Behavior uses the present tense and the active voice.

The DocFX site publishes `docs/`, and a package readme is shown on NuGet outside the repository, so links need
the right target:

- A page links another page by its relative path.
- A file outside `docs/`, such as `AGENTS.md`, `BACKLOG.md` or a source file, links to its branch URL,
  `https://github.com/Nickelony/LanguageServer/blob/main/<path>`, because the site does not publish it.
- A package readme links every target by its branch URL.
- A link points at the branch, never a tag, so the reader lands on the current text.

### READMEs

A package readme is a front page, not a manual: a one-line summary, install, one or two runnable examples, then
the few facts the signature does not carry. Install and a first successful command come before any explanation.

- Show the call instead of describing it.
- Never inventory types, members or options; the editor's member completion and the API reference are the
  inventory. A table a test keeps in sync with the code is the exception.
- Keep the prose to about one screen, and link the guide that owns any deeper fact.

### Living documentation

- The live set is `README.md`, the per-package `README.md` files, the `docs/` guides, `BACKLOG.md`,
  `CONVENTIONS.md`, `REVIEWING.md`, and this file. These track the code, so update the affected one in the same
  change that invalidates it.
- `BACKLOG.md` is the only record of open work: one issue-shaped entry per item, with no completed work and no
  process narration, deleted when the work lands. A plan or review that has served its purpose is deleted, not
  kept. Nothing else in the tree records history.
- A design note that outlives its originating wave is rewritten as durable rationale. The wave identifiers and
  file-by-file estimates come out, and the model and the rejected alternatives stay. The note lives in `docs/`,
  and the guide that owns its topic links it. `docs/KeyBindingContextDesign.md` is the worked example.
- Superseded material is deleted, not kept in the tree.

### Shape

- New and rewritten Markdown is wrapped by hand at 110 columns; text a change does not touch is reflowed when it
  is next edited. `///` comments wrap at `stanza_max_line_length`, and the tooling does the wrapping.
- A hand-wrapped run flows clause to clause: a line carries the clause that fills it even when that overshoots
  the width, and the mark closing the clause stays on the line. A wrapped line opens on a word, never on a mark
  or a list bullet.
- US English. Write " - ", never a typographic dash such as an em or en dash.

## Documentation shape

Length is judgment, not a quota. Write as short as the facts allow, and split a page that stops being a
one-sitting read. The aims below guide a run's length; they are not limits:

- An XML doc block aims for at most 8 `///` lines; an inline comment run aims for at most 6 `//` lines.
- A paragraph aims for at most 100 words outside fences, tables and lists. A list item aims for at most 50 words.
- A sentence aims for 25 words, and one past 50 is worth splitting.

Protocol and type names often cannot be shortened, and no fact is cut to meet an aim. Split a run past its aim
where the split reads better, and keep it whole where it does not.

`DocumentationGuardTests` fails the test run on a banned phrase or a British spelling. The banned phrases include
the typographic dashes and, in comments, second person. `DocumentationReportTests` reports every run past an
aim without failing, as a test of the `RepositoryReport` category. To read that report, run that category with
detailed verbosity:

    dotnet test .\Nickelony.IDEKit.slnx --filter TestCategory=RepositoryReport --logger "console;verbosity=detailed"

The report test reads each aim from this section, so keep the wording the test matches.
