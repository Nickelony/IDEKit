# Architecture guide

This page states the rules that place every package, folder and namespace in the repository, and the reason for
each. Read it before you add a project, a project reference, a namespace, a `shared/` folder or an
`InternalsVisibleTo` grant. The [home page](index.md) describes the package families; each sits in the architecture
as follows:

- `Nickelony.IDEKit.*` holds the editor-side tiers, from the engine-free primitives to the editor bindings.
- `Nickelony.LanguageServer.*` holds the language-server client, the provider framework and the language packages.
- `Nickelony.KeyBindings.*` holds a command and shortcut system that references nothing outside its own family.
- `Nickelony.QuickPick.*` and `Nickelony.Matching` hold a quick pick for a command palette or a quick open, and the
  fuzzy matching it ranks with. They reference nothing outside their own family.

## Tiers

A **tier** is a level of the package graph, and a package references only packages in a lower tier:

```text
 editor bindings        LanguageServer family       KeyBindings family
 (one engine each)      (client and providers)      (references only itself)
        |                        |
        +-----------+------------+
                    v
        neutral contracts over Core and QuickPick
              |                  |
              v                  v
 Core (engine-free,       QuickPick family
 references nothing)      (references only itself)
```

The neutral tier holds the packages over Core that name no editor engine and no language server.
`LanguageFeatures` holds the feature contracts, their request and payload records, and the vocabularies they
share. `LanguageFeatures.Controllers` holds the editor-side behavior that drives those contracts: the
controllers, and the engine-free machinery they drive, such as the completion sessions, the snippet expander and
the definition resolvers.

The package is named for its controllers because each feature centers on one, as each editor contribution of VS
Code's `src/vs/editor/contrib/` centers on a controller (`SuggestController`, `CodeActionController`) with its
sessions and models beside it.

`Workspace` holds the document authority, and `Workspace.QuickPick` holds the quick-open file list, which follows
the file system through the `FileWatching` leaf. The two are separate packages because a leaf such as `FileWatching`
holds one job: a host that only stores documents takes no quick-pick or file-watching package. The tiers keep a host
from taking more than it uses, and they keep a lower package from changing because a higher one does:

- A LanguageServer package reaches IDEKit only through Core, LanguageFeatures and a neutral leaf that
  references only Core (`FileWatching`), so a language-server host takes no editor engine and no controller.
- An editor binding never references a LanguageServer package, so a host that wants an editor control takes no
  language server.
- A KeyBindings package references KeyBindings packages only, so the command system works in any application.
- A QuickPick package references QuickPick packages and `Nickelony.Matching` only, so a command palette works in
  any application, with no editor and no IDEKit package.
- A package that serves one narrow job, such as the language-server client, the process runner or the fuzzy
  matcher, references nothing.
- Only `Nickelony.IDEKit.Workspace.Views` references `Nickelony.IDEKit.Workspace`: an editor binding takes no
  document authority, so the engine-free `WorkspaceDocumentTextViewBase` of `Workspace.Views` connects an
  editor to the store, and a host derives its editor view from it.
- `Workspace.Views` also references LanguageFeatures, because `WorkspaceEditApplier` applies a language-server
  `WorkspaceEdit` to the tracked documents and `WorkspaceDocumentSynchronizer` mirrors the documents that have a
  view to an `IDocumentSynchronizationTarget`, such as a language server.
- The `GuardNoWorkspacePackageReferences` target of `Directory.Build.targets` fails the build of an IDEKit,
  KeyBindings, QuickPick or Matching package other than `Nickelony.IDEKit.Workspace.Views` that references
  `Nickelony.IDEKit.Workspace`, and the tier rules keep the LanguageServer packages away.

The `Repository` guard tests keep these references honest. `ProjectReferenceGuardTests` holds the table of
references that each library may take, so a new reference is a decision made in that table, and the dependency
diagram of the root
[README](https://github.com/Nickelony/LanguageServer/blob/main/README.md#dependency-diagram) must match the
project files. A library that references the provider framework is a language package and needs no row.

`Core` references nothing, and its types group by their reader. The [Core guide](CoreGuide.md) owns the rules
of each area, and the namespaces are the inventory:

- The text model that every package reads: snapshots, ranges, positions, line maps and the edit kernel.
- The host-facing engines that bindings and hosts call, such as comment scanning and planning, auto-closing,
  formatting and indentation, identifiers, find and replace, diffing, highlighting rules, bookmarks, line
  status and navigation.
- The engine-free primitives that several packages share, such as threading, request coordination, local paths,
  color, validation, callback containment and change notifications.

The quick pick is not an editor feature: its controller needs the synchronization context of the thread that
creates it and nothing else, and its window is drawn against any element of a host. It is a family of its own, so
a host with no editor, such as a 3D view, takes a command palette without a package of IDEKit.

The modes that list what an IDEKit package owns implement the provider contract of the family in that package. The files mode sits
in `Workspace.QuickPick`, which owns the file list, and the go to line, open editors and symbol modes sit in
`LanguageFeatures.Controllers`, which drives `IDocumentSymbolProvider` and `IWorkspaceSymbolProvider`. Core
cannot hold them, because it would then reference the family and lose its place at the bottom of the tiers.

`Core.Bookmarks` holds the one file-persistence seam of `Core` (`IBookmarkStore`, with the sidecar file system
behind it), because a bookmark set is the only `Core` data that outlives its document session, and the document
authority in `Workspace` stores text, not annotations. Every other `Core` type works on text it is given.

A type belongs in `Core` when it names no engine type, needs no dependency that `Core` lacks, and is either
about editor text or used by two or more packages. A job that is not about editor text and that one family
needs, such as process running or JSON Schema indexing, stays a leaf package under the product name
(`Nickelony.IDEKit.Processes`), so a host takes it without taking the editor packages. The package groups of
the root [README](https://github.com/Nickelony/LanguageServer/blob/main/README.md#package-groups) are a
catalog of what ships, whereas a tier is a dependency level.

`Nickelony.LanguageServer.Provider.Abstractions` holds the contract of a language provider that an editor host
codes against: `ILanguageServerFeatureProvider`, its capability snapshot and its failure payloads. A host, a
test double and a second implementation of that contract reference it without the client or the provider
framework, so the contract is the one thing that the implementations must agree on.

The family ships in lockstep. Every package carries one version, and `dotnet pack` writes each dependency on another
in-repo package as an exact range, so restore reports a host that mixes versions instead of leaving a
`MissingMethodException` or a `TypeLoadException` for run time. A package that read another package's internals
would break at run time on any mismatch, so the [internals rule](#internals) keeps them private.

## Slices

A **slice** is one feature, named once and used by every package that holds a piece of it. The name comes from the
LSP or from the editor feature (`Completion`, `Hover`, `Bookmarks`), and it names the feature's folder in each
package and, in IDEKit, its namespace. A reader who knows a feature's name finds its files in every tier, and renaming
the feature changes one word.

- `Completion` is a folder in the client's wire model (`Protocol/Completion`), in the neutral
  `LanguageFeatures` package and in each binding's language-features package, and it is a namespace in
  `LanguageFeatures` and in each of those binding packages.
- `Bookmarks` has no language-server half: it is a folder and a namespace in Core and in each base binding.

A slice name can exist in both Core and LanguageFeatures with different meanings, because they model the same
feature at different levels:

- `Diagnostics` is the render primitive of a diagnostic span in Core and the diagnostic payload in LanguageFeatures.
- `Editing` is the offset-based edit kernel in Core and the LSP edit payloads in LanguageFeatures.
- `Formatting` is a synchronous text-to-text formatter in Core and an asynchronous provider of edits in
  LanguageFeatures.
- `Navigation` is caret history and the navigation target of an editor in Core and definition and reference
  locations in LanguageFeatures.

Core is offset-based, local and synchronous, and LanguageFeatures is shaped by providers: its requests and most
of its payloads address offsets, and the locations that name a place in another document use positions.
`SliceGuardTests` lists these four names, so a fifth shared name fails until it carries a reason there.

A span of text is an offset range (`TextRange`) in Core's edit kernel and a line and character range
(`TextPositionRange`) in the payloads of LanguageFeatures that address a position, because the edit kernel works on
offsets and a language server speaks positions.
`TextLineMap` converts between them, and a binding or a provider converts at its own boundary.

A feature has one request contract, and it lives in LanguageFeatures beside the feature's request and result
records: `IHoverProvider` is the only interface that answers a hover request, so a language server, a local
catalog and a test double all implement the same one. The language-server family inherits that contract
instead of declaring a twin, and a feature whose results reach the host without a request is a source in the
same slice (`IDiagnosticsSource`). A consumer of one feature depends on that feature's interface alone and
mocks it alone.

## Namespaces

A namespace holds what a consumer imports together, and a folder serves the maintainer. The rule has two outcomes,
which follow from how a consumer uses a package:

- An IDEKit package that holds several features has one namespace per slice, and each namespace mirrors a
  folder. The slice name is the same in every tier, and a host that uses a feature imports that feature's
  namespaces from each package it takes.
- A LanguageServer, KeyBindings, QuickPick or Matching package has one namespace, because a host uses it as a
  whole. Its folders express slice ownership and layering only, apart from the two second namespaces named below.

A narrow IDEKit package with one concept, such as TextMate or Processes, keeps all its files in one
folder, so its folder and its namespace agree. A narrow package takes feature namespaces instead when it
serves two audiences or holds more than 30 public types, because one namespace then makes every audience
import the types of the others; `PublicTypeBoundGuardTests` counts the public types of every namespace.

`Workspace.Views` keeps its view contract and its failure codes in `Nickelony.IDEKit.Workspace.Views`, and the
coordinator and its results sit in `Nickelony.IDEKit.Workspace.Views.Coordination`. `Markdown` keeps the host-facing
options, theme and pipeline in `Nickelony.IDEKit.Markdown`, the content model in
`Nickelony.IDEKit.Markdown.Model`, and the render plan that the emitters read in
`Nickelony.IDEKit.Markdown.Planning`.

A folder of an IDEKit package or of a shared editor home whose files declare only internal types keeps the
namespace of its parent folder. Folder-derived namespaces and the one-direction rule below would otherwise give the machinery behind a
slice a namespace of its own that the slice's own types name back, and a guard checks that each type in such a
folder is internal.

One LanguageServer package has a second namespace because it serves two audiences.
`Nickelony.LanguageServer.Client` is the facade that a host driving a provider imports. Its LSP wire model,
every type under its `Protocol/` folder, lives in `Nickelony.LanguageServer.Client.Protocol`, which only
protocol-level code imports. Outside Client, a file therefore names a wire type only when it imports the wire
namespace. A host that drives a provider imports the facade alone, and the facade exposes a wire type through
`DiagnosticsPublishedEventArgs.Parameters` and `NegotiatedServerCapabilities.TextDocumentSyncKind`, so a host
that names either type imports the wire namespace as well.

The KeyBindings package has a second namespace for the entries of its override document. A host names
`KeyBindingOverrides` and `IKeyBindingOverrideStore`, because it persists the document through a store of its
own, but it reaches `KeyBindingOverrideEntry`, `KeyBindingOverrideChord` and `KeyBindingOverrideStroke` only through
the document, and a serializer reads them through the properties of the document without naming them. They sit
under `Overrides/Entries/` in `Nickelony.KeyBindings.OverrideEntries`, which only code that reads or edits an
entry imports. The common path keeps one import, and the root namespace keeps room under the bound of 30 public
types that `PublicTypeBoundGuardTests` counts.

A model type and a wire type may share a name, such as the diagnostic severity enums, because each name
follows its own specification. Only Provider sees both, and a file there qualifies the wire side through the
`Lsp` alias. Each such enum pair has one `...Conversion` that maps it through an explicit `switch`, because
the tier wall forbids one shared type and a cast would let a value that only one side defines cross.

The namespaces of one package with feature namespaces depend on each other in one direction, so a reader can follow
the package from its root outward. A nested namespace sees its parent without an import, so the compiler alone does
not keep that direction, and a `Repository` guard test fails a cycle.

The folders of a package with one namespace depend on each other in one direction too, for the same reason.
The namespace cannot show that direction, so the folder graph is the only structure to keep, and a
`Repository` guard test fails a cycle between folders. Files at the package root stand outside the graph: the
package root holds the facade and the types that several folders share.

No consumer imports a test type, so the folders of a test project organize files without adding namespaces:
each test file declares the name of its project, and a `Repository` guard test checks it.

The .NET design guidelines state the same principle: they group namespaces by the scenarios a consumer uses together,
and they treat folder and assembly layout as separate concerns. Roslyn keeps `Solution`, `Project` and `Document` in
`Microsoft.CodeAnalysis` while their files sit under `Workspace/Solution/`, `Microsoft.VisualStudio.LanguageServer.Protocol`
ships flat, and AvalonEdit splits its namespaces by feature.

The per-file namespace rules are in
[CONVENTIONS.md](https://github.com/Nickelony/LanguageServer/blob/main/CONVENTIONS.md#namespaces), because a contributor applies
them to each file. The two editor bindings share their identical source through linked files, and the
[editor sharing guide](EditorSharingGuide.md) owns how those files pick each binding's namespace.

### Shared primitives

A primitive that several slices share sits in a slice of its own, even when that slice holds one type:
`Core.Media` holds the color that highlighting and the Markdown themes both use. Folding it into one
consumer's slice would make every other consumer depend on that slice, and a one-type namespace costs a host
one `using`; `Core.Validation` is the same case for the numeric checks that the options records of several
packages share.

`Core.Callbacks` holds the containment of host callbacks that several packages run, and `Core.Notifications` is
the same case for `IChangeNotificationSource`, which line status, bookmarks and the code-action controller each
implement and no one of them owns. A public type sits in the root namespace of a package with feature namespaces
only when every namespace of the package uses it and its name carries the package concept.

`Core.Media` and `Core.Threading` mirror the toolkit namespaces of the same name on purpose, because they hold
the neutral counterparts of toolkit types (`ArgbColor`, `IUiDispatcher`), and the namespace tells a reader
which side a file uses.

## One home for a rule

A rule that two families need lives in a package both reference. Where one family must not reference the other
(the client references no project, so it cannot take Core), the rule lives in one internal source file under a
neutral `shared/` folder that both compile, never in two hand-kept copies that drift apart.

A primitive that a standalone family needs from Core is not shared by a reference, because the family references
nothing outside itself. Its source sits in `shared/Primitives/` and both compile it: Core declares the public type in
its slice namespace, and the QuickPick family compiles the same file with `QUICKPICKFAMILY` set, which selects the
family's flat namespace and makes the type internal.

Core's type is the only public copy, so a host that takes Core and the family sees one public type of the name,
and the tests of Core cover the one body. A body that names a Core type the family cannot see compiles through an
alias in the header of its file: the debouncer reads the dispatcher of the family as its `IUiDispatcher`.

Source is linked across packages in two cases only: a helper is needed by two packages that no package both
reference could hold, as `LocalPathRules` is for Core and Client, and a primitive of Core is needed by a standalone
family, as above. Every other helper that several packages need is public API of the lowest package they reference,
with a direct test, because a shipped package reads no other package's internals.

A helper with state or logic, such as a gate, is public API of that package whoever calls it, and a type with a
host-facing role compiles once, in that package. A helper that names an engine type compiles once per engine, in
the base binding, which declares it in its `Media` namespace, and the feature bindings reach it through the public
`ToBrush` and `ToColor` conversions.

Each linked helper of an internal-helper home declares the one flat `Nickelony.Infrastructure` namespace, so it
has a single identity in every assembly that compiles it. The local-path identity rules are the worked example:
`shared/Infrastructure/Paths/LocalPathRules.cs` compiles into Core and Client, Core's `LocalPath` and
`LocalPathComparisonPolicy` present it to a host, and the client's `DocumentUriConversion` presents its
file-URI side.

## Internals

A shipped package reads no other package's internals. A package that extends another one, such as a language provider
built on the provider framework, uses only the public or protected surface that package documents, so the same route
stays open to a host and stays tested by the repository's own packages. `InternalsVisibleTo` names only test
assemblies.

## Log event ids

A package whose log events are a host-facing contract takes a numeric block for its event ids, and the blocks
are disjoint, so an id identifies one event across these packages:

| Packages | Block |
| --- | --- |
| The LanguageFeatures controllers package and its binding packages | 1000-1059 |
| The Markdown package and its binding packages | 2000-2002 |
| The TextMate package | 3000-3004 |
| `Nickelony.KeyBindings` | 4000-4005 |

A package that adds host-facing events takes an unused block and adds it to this table. Inside the Controllers
block, each feature takes its own decade (1000, 1010, and so on), so the block's upper bound grows by a decade
per feature. The id and name of each log are declared on the `LoggerMessage` attribute that produces it.

## Evolution after publication

Once a package is published, a breaking change to its public surface costs a major version, and an additive
change ships in any minor release. A change is breaking when a host that compiled against the previous version
fails to build, fails to bind at run time, or sees different behavior, so the classes are:

| Change | Class |
| --- | --- |
| A new type, or a new overload of a method | Additive |
| A new optional, init-only property on a record or class (not `required`) | Additive |
| A new virtual member on a public base type | Additive |
| A new member of an enum whose documentation says it can grow | Additive |
| A new interface member with a default implementation | Additive |
| A new sibling interface that a type implements beside the one it has | Additive |
| A rename or removal of a type or member, a parameter rename included, because a named argument binds to it | Breaking |
| A changed default value or default behavior, output a host can observe included | Breaking |
| A new positional record parameter, or a new `required` member | Breaking |
| A new optional parameter on an existing method, because a compiled caller binds to the old signature | Breaking |
| A new abstract member on a public base type, or a new interface member without a default implementation | Breaking |
| A reordered or renumbered enum member | Breaking |
| A stricter validation, or a new exception from an existing member | Breaking |

A new interface member takes a default implementation, which every target framework of the repository supports;
where a default cannot be written, the new member goes on a sibling interface that a host opts into. An enum
states in its documentation when it can grow, so a host knows to keep a default branch in a `switch` over it.

An aggregate interface that a host implements, such as `ILanguageServerFeatureProvider`, never gains a base
interface, because that adds abstract members to every implementer. A feature added after publication arrives as a
sibling interface that the provider also implements, and a consumer reads it with a type test (`as` or `is`), so a
decorator or a fake that lacks it stays valid.

Every entry of [BACKLOG.md](https://github.com/Nickelony/LanguageServer/blob/main/BACKLOG.md) is additive by
this table.

Every package targets `net8.0`, and the WPF packages target `net8.0-windows`. An application on a later .NET
consumes them unchanged. No package targets `netstandard2.0`, because the code relies on the
`ArgumentNullException.ThrowIfNull` family and on language and library features that neither .NET Standard
version has, and .NET Framework hosts are not the audience. Raising the floor is a target-framework change,
which is breaking for a host that stays on the older runtime, so it waits for a major version.

## Terms

### Binding

A **binding** connects the neutral editor logic to one editor engine: the AvalonEdit packages for WPF, or the
AvaloniaEdit packages for Avalonia. Each binding has a base package and feature layers, such as language features,
Markdown and TextMate, and the same feature exists in every binding. The editor bindings are named for the
editor control they adapt, because that control and not its toolkit is what a binding wraps. In contrast, the
key-binding adapters and the quick-pick windows are named for the toolkit (`Wpf`, `Avalonia`) because they adapt its
input events and draw its controls.

### Workspace

A **workspace** is the files a host edits, rooted in the folders it opens, as in VS Code's `workspace` and the LSP's
workspace folders. `Nickelony.IDEKit.Workspace` is the authority over its documents, the provider reports its folders
and file changes to the server, and a workspace edit changes several of its files.
