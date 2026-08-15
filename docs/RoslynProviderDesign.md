# Roslyn-backed language providers - design rationale

This note records why the family's second language is Visual Basic on Microsoft's Roslyn language server,
the shape that decision produced (a shared Roslyn core plus thin language packages), the framework work
it required, and the alternatives that were weighed. It is the durable rationale for
`Nickelony.LanguageServer.Roslyn` and the packages over it - `Nickelony.LanguageServer.VisualBasic` and the
`Nickelony.LanguageServer.CSharp` sibling that later reused the same core; the outcome of the work is
recorded in `CHANGELOG.md`, and the compatibility checklist a candidate server must fit is in
[Provider authoring](ProviderAuthoring.md) and section 5 of `future-backlog-2026-09-17.md`.

## 1. Goal

Add VB.NET editor IntelliSense to the `Nickelony.LanguageServer.*` tier as a sibling to `.Lua`: a provider
a host constructs with workspace roots and a server executable path, then drives through the same
`ILanguageServerIntelliSenseProvider` surface (completion, hover, definition, references, rename,
formatting, signature help, document symbols, code actions, diagnostics). Visual Basic was the first target;
the C# sibling followed over the same core, which is the evidence that the shared shape was the right unit.

## 2. Why Roslyn, and why there is no VB-only server

There is no standalone "VB.NET language server". Microsoft's **Roslyn language server**
(`Microsoft.CodeAnalysis.LanguageServer`, published on nuget.org as `roslyn-language-server` by
Microsoft/RoslynTeam) serves **C# and VB from one process** - the same server VS Code's C# extension and
C# Dev Kit launch. So "VB.NET support" means driving the Roslyn server and setting the document language to
`vb`; it is not a project that stands on its own the way the LuaLS provider does.

The direct consequence: the natural unit is a **shared Roslyn core** plus thin per-language packages, not a
VB-only package that duplicates the handshake and feature mapping.

## 3. Chosen shape

- `Nickelony.LanguageServer.Roslyn` - the Roslyn-specific handshake: capability, initialization, and settings
  factories; the launch profile (`--stdio --autoLoadProjects [<n>] --telemetryLevel off`, where the value is
  appended only for a positive maximum and `0` omits the switch); project/solution awareness;
  and the pull-diagnostics integration (section 5).
- `Nickelony.LanguageServer.VisualBasic` - thin: `LanguageId = "vb"`, the source and project watch specifications
  (`*.vb`, `*.vbproj`; the core appends the shared solution, project-asset, build-properties, and build-packages patterns),
  and the option passthrough - it reuses `RoslynLanguageServerOptions`, because one server serves both
  languages and the settings document already carries both the `csharp|` and `visual_basic|` sections, so a second
  options record would be unproduced surface.
- `Nickelony.LanguageServer.CSharp` - the same core with `LanguageId = "csharp"`, the `*.cs` and `*.csproj`
  watch specifications, and the same `RoslynLanguageServerOptions` reuse. It is a near-copy of the Visual
  Basic package, which is the point: the core absorbed everything but the identity, so the second language
  cost one small package rather than a second provider implementation.

This matches the family's inward-pointing dependency shape (Abstractions <- Client <- Provider <- concrete
provider) and its small-public-surface convention (Lua exposes two public types, C# and Visual Basic one each).

## 4. Fit with the family's server constraints

The shape fits the fixed choices the client makes, whose full list is in
[Provider authoring](ProviderAuthoring.md). Positions are UTF-16 native, dynamic registration is not
needed for the features the provider exposes (the client advertises `dynamicRegistration = false` and the
server serves those features from its static capability set), and the fixed-multi-root model is exactly
what `--autoLoadProjects` consumes.

The one constraint that did **not** fit was diagnostics: the client was push-only and Roslyn is
pull-oriented, which is the framework work in section 5. The server's process working directory is not a
constraint, because project discovery is driven by the LSP workspace folders rather than by the working
directory.

## 5. Pull diagnostics

The Lua provider's server pushes diagnostics (`textDocument/publishDiagnostics`), and the framework
consumed only that model: the payload arrived as `PublishDiagnosticsParams` and reached the provider
through the `HandleDiagnosticsPayload` hook. The Roslyn language server is **pull**-oriented: it delivers
diagnostics through `textDocument/diagnostic`, with a `resultId` the client echoes back as
`previousResultId`, and asks the client to re-query through `workspace/diagnostic/refresh`.

Supporting it was the largest piece of the work, and it landed in the framework rather than in the new
package because it is not Roslyn-specific:

1. `Client`: the `textDocument/diagnostic` payloads (a tolerant report type covering the full, unchanged,
   and related-document shapes), the `diagnosticProvider` capability read, and the
   `workspace/diagnostic/refresh` server request.
2. `Provider`: a capability-gated pull loop that, after every successful synchronization and on a server
   refresh, requests the report, threads each report's `resultId` back as the next `previousResultId`,
   applies a full report through the same diagnostics hook the push path uses, and keeps the cache on an
   unchanged report.

A provider therefore implements one diagnostics hook and gets both delivery models. Lua is unaffected
because it advertises no pull capability, and any future pull-only server benefits without new provider
work.

## 6. Project and solution awareness

Roslyn is solution/project-based, where Lua is "loose files under a workspace root". Three models were
considered:

- **Require and load a project or solution** - a stricter contract than the family's fixed-root, loose-file
  model, and one that would put project-selection policy in the library.
- **Rely on `--autoLoadProjects`** with the server's miscellaneous-files behavior for a file that is in no
  project - chosen.
- **Send `solution/open`/`project/open`** notifications - needs new framework surface, and their ordering
  against `initialize` and the workspace-folder advertisement is a host concern.

The chosen model keeps the framework's fixed-root shape: the launch profile enables automatic project
loading, the roots are advertised in `initialize`, and the server discovers the solutions and projects
below them. A loose source file lands in the server's miscellaneous-files mode, where cross-file features
are reduced; that limitation is documented in the package READMEs rather than papered over.

## 7. One shared feature layer, not two provider implementations

The per-feature request-and-parse for a standard LSP server is language-neutral: the Lua provider's feature
implementations and its `ResponseParser` partials map the family's own protocol payloads onto the shared
editor models, and nothing in them is Lua-specific. A second provider would have duplicated that code.

The layer therefore moved into the framework. The standard feature implementations are `virtual` members on
the provider base; the response mapping and the cancellation-safe parsers live in the `Mapping` slice of
`Nickelony.LanguageServer.Provider` as internal shared types, exposed to the provider packages
through `InternalsVisibleTo`; and the shared per-document payload cache became
`ServerPayloadDocumentStore`, which Lua and Roslyn both use. Keeping the mapping internal avoids committing
a large new public surface before an external provider needs it; it is promoted to public only then.

The same reasoning produced the settings split: the framework owns the pipeline, and each package owns only
what is language-specific - the settings payload, the configuration-file rule, the failure wording, and the
watch patterns.

## 8. Where the server executable comes from

The library takes a `serverExecutablePath` and stays out of acquisition, exactly like LuaLS. The host has
three options:

| Option | Notes |
|---|---|
| **Host-supplied path (recommended)** | Parity with LuaLS. The host installs or points at `roslyn-language-server` (a .NET tool: `dotnet tool install -g roslyn-language-server --prerelease`) or at a self-contained build. Zero redistribution, zero package size, host owns updates. |
| **Optional runtime downloader (recommended convenience)** | An opt-in host helper that fetches the matching `roslyn-language-server.<rid>` package from nuget.org into a local cache on first use - the pattern VS Code extensions use. No re-hosting; keeps the package small. |
| **Bundled per-RID payload (not recommended)** | The self-contained RID packages are large (~68 MB for `win-x64`); the neutral tool additionally needs the .NET 10 runtime; the current line is prerelease. |

**Licensing.** The Roslyn language server is MIT-licensed (part of the .NET Compiler Platform), so
redistribution or bundling is permitted provided the copyright and license notice is retained - bundling is
not a terms-of-service breach in itself. The reasons to avoid it are practical rather than legal: package
size per RID, ownership of the update and security cadence, pinning a prerelease, and the runtime
requirement of the neutral tool. The recommended split keeps the library acquisition-free; a downloader, if a
host wants one, is a host concern rather than package surface (section 9).

## 9. Decisions and deferred alternatives

- **Pull diagnostics**: added to the framework rather than gated off per provider (section 5).
- **Project model**: `--autoLoadProjects` plus advertised roots, with no `solution/open` in this cut
  (section 6).
- **A `.CSharp` sibling**: built once the Visual Basic package proved the shared-core shape - a near-copy of
  it over the same core, adding only the identity and the `*.cs` / `*.csproj` watch patterns.
- **An optional runtime downloader**: not part of this family. Acquisition stays consumer-supplied
  (section 8); a downloader is a host convenience and would add a network dependency and an update cadence
  to the library.

## 10. References

- [Provider authoring](ProviderAuthoring.md) - the provider recipe and the server compatibility constraints
  this design follows, including the shared-core pattern.
- [Consumer integration](ConsumerIntegration.md) - the host-facing contract the provider honours.
- `CHANGELOG.md` - the outcome of the work this note planned.
