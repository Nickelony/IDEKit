# Roslyn package - design rationale

C# and Visual Basic share one profile factory, `RoslynLanguageServerProfileFactory` in
`Nickelony.LanguageServer.Roslyn`, which a `RoslynLanguage` selects the language of. This note gives the rationale for that
shape and for the alternatives it rejected. The [Roslyn guide](RoslynGuide.md) owns the mechanics:
server acquisition, launch arguments, settings, watch sets, and the pull-diagnostics model. The compatibility
checklist a candidate server must fit is in [Provider authoring](ProviderAuthoring.md).

## 1. One package for C# and Visual Basic

The Roslyn profile is a sibling of the Lua profile: a host creates one for a language, passes it with the
workspace roots and a server executable path to `LanguageServerFeatureProvider`, then drives the provider through
the same `ILanguageServerFeatureProvider` surface.
One Roslyn language server serves both languages ([Position](RoslynGuide.md#position)), so the handshake, the
launch arguments, and the settings document are shared, and a `RoslynLanguage` selects only a language identity
and its watch patterns.

A package per language would duplicate the handshake, the launch arguments, and the settings document, and two
profile factories over a shared core would still repeat the factory body, so the design rejects both. The chosen
shape keeps the family's dependency order (`Provider` references `Provider.Abstractions` and `Client`, and the
Roslyn package references `Provider` and `Client`) and a small public surface: one factory, one language selector
with two values, and the session options.

The selector is an enum and the descriptor behind it is internal, because the Roslyn language server serves only
C# and Visual Basic, so the two values are the whole vocabulary. A host that serves another language builds its
own profile on `Provider`'s authoring surface.

## 2. Fit with the family's server constraints

Roslyn fits the fixed choices the client makes, whose full list is in [Provider authoring](ProviderAuthoring.md).
Its positions are UTF-16 native, and it serves the features the provider exposes from its static capability set,
so the client's `dynamicRegistration = false` costs no feature. The server's process working directory is not a
constraint either, because project discovery follows the LSP workspace folders rather than the working directory.

## 3. Pull diagnostics in the framework

LuaLS pushes diagnostics through `textDocument/publishDiagnostics`, while the Roslyn language server is
pull-oriented ([Diagnostics](RoslynGuide.md#diagnostics)). Pull diagnostics live in the framework rather than in
the Roslyn package, because the delivery model is not Roslyn-specific:

- `Client` carries the `textDocument/diagnostic` payloads, the `diagnosticProvider` capability read, and the
  `workspace/diagnostic/refresh` server request.
- `Provider` runs a capability-gated pull loop that applies each full report through the diagnostics path the
  push delivery uses.

A provider therefore implements one diagnostic mapping policy and gets both delivery models. Lua is unaffected
because it advertises no pull capability, and a future pull-only server needs no new provider work.

## 4. Project and solution awareness

Roslyn is solution- and project-based, where Lua works on loose files under a workspace root. Three
models are compared:

- **Require a project or solution from the host** - a stricter contract than the family's fixed-root,
  loose-file model, and one that would push project selection onto every host.
- **Rely on `--autoLoadProjects` alone** - the server can discover the projects below the workspace folders
  without loading them, so a source file stays a file-based program with implicit usings and the
  diagnostics a project would raise go missing.
- **Send `solution/open` or `project/open` right after `initialized`** - chosen.

The chosen model keeps the framework's fixed-root shape: the roots are advertised in `initialize`, and the
profile turns them into the notifications the C# extension of Visual Studio Code sends, one solution when a root
holds exactly one, otherwise the language's project files. The notifications travel through a framework seam,
the client's `SessionStartNotificationsProvider`, which the profile fills from its
`BuildSessionStartNotifications` hook. The client sends them on every session start, after `initialized` and
before the session reports ready, so they precede every document notification and a restart reloads the
projects without host code. A factory fault is logged and never fails the start, because a session without
projects still serves loose files.

`--autoLoadProjects` stays on the command line next to the notifications. The two share
`AutoLoadProjectsMaximum`, the server accepts both in one session, and the switch keeps project discovery for a
server that loads what it discovers. The [Project loading](RoslynGuide.md#project-loading) section of the guide
owns the rules, and a file in no project keeps the reduced cross-file fidelity that the guide lists under
[Limitations](RoslynGuide.md#limitations).

## 5. One shared feature layer, not two provider implementations

The per-feature request-and-parse for a standard LSP server is language-neutral: it maps the family's own
protocol payloads onto the shared editor models, and nothing in it is specific to one language. The layer is
shared rather than reimplemented per provider:

- The standard feature implementations are internal providers that the sealed provider exposes through its
  contract members, which every language package gets as they are.
- The response mapping and the cancellation-safe parsers are internal types of
  `Nickelony.LanguageServer.Provider`, so a language package gets them through the provider.
- The document tracker and its per-document payload cache are internal framework types, so Lua and Roslyn
  share them through the provider.

Keeping the mapping internal avoids committing a large new public surface before an external provider needs it;
it is promoted to public only then.

The same reasoning produced the settings split: the framework owns the pipeline, and each package owns only
what is language-specific - the settings document, the configuration-file rule, the failure wording where it
differs from the default, and the watch patterns.

## 6. Where the server executable comes from

A host has these options for the server executable, because the library stays out of acquisition
([Server acquisition](RoslynGuide.md#server-acquisition)):

| Option | Notes |
|---|---|
| **Host-supplied path (recommended)** | Parity with LuaLS. The host installs or points at `roslyn-language-server`. Zero redistribution, zero package size, and the host owns updates. |
| **Optional host runtime downloader (not library surface)** | An opt-in host helper that fetches the matching `roslyn-language-server.<rid>` package from nuget.org into a local cache on first use - the pattern editors commonly use. No re-hosting, and the package stays small. |
| **Bundled per-RID payload (not recommended)** | The win-x64 and linux-x64 RID packages are each large `.nupkg` files. They hold the server's assemblies, symbols and XML docs but not the .NET runtime, so the server also needs a separately installed .NET runtime ([Server runtime](RoslynGuide.md#server-runtime) names the version). |

**Licensing.** The package README states that the tool is part of the .NET Compiler Platform (Roslyn) and is
licensed under the MIT license. The MIT license requires the copyright and permission notice in all copies or
substantial portions of the software, and the package carries that notice in `ThirdPartyNotices.rtf`, so a
bundled copy keeps the notice by shipping that file.

The reasons to avoid bundling are practical rather than legal: package size per RID, ownership of the update and
security cadence, pinning a prerelease server version into the package, and the .NET runtime requirement of the server.
The recommended split keeps the library acquisition-free. A downloader would add a network dependency and an
update cadence to the library, so it stays a host concern rather than package surface.

## 7. References

- [Roslyn guide](RoslynGuide.md) - the acquisition, options, launch arguments, settings, watch sets,
  diagnostics, and limitations of the Roslyn package.
- [Provider authoring](ProviderAuthoring.md) - the provider recipe and the server compatibility constraints
  this design follows.
- [Consumer integration](ConsumerIntegration.md) - the host-facing contract the provider honors.
