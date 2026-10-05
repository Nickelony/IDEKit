# Roslyn guide

`Nickelony.LanguageServer.Roslyn` drives Microsoft's **Roslyn language server** (`roslyn-language-server`), the
LSP server that powers the C# and Visual Basic experience in editors. One profile factory,
`RoslynLanguageServerProfileFactory`, configures `LanguageServerFeatureProvider` for either language.

This guide owns the server acquisition, the session options, the launch arguments, the project loading, the
settings document, the pull-diagnostics model, the watch sets, and the limitations.

The rationale for one package that serves both languages is in the [Roslyn design note](RoslynProviderDesign.md); the host-facing
lifecycle, threading, and disposal contract is in the [consumer integration guide](ConsumerIntegration.md). A running host is
[`SampleLanguageServers`](https://github.com/Nickelony/LanguageServer/blob/main/shared/Samples/LanguageServers/SampleLanguageServers.cs).

## Position

There is no standalone C# or Visual Basic language server: the Roslyn language server serves both languages
from one process. The handshake, the launch arguments, and the settings document are therefore shared, and a
[language](#languages) contributes only its identity and watch patterns:

```csharp
LanguageServerProfile profile = RoslynLanguageServerProfileFactory.Create(
    RoslynLanguage.CSharp,
    new RoslynLanguageServerOptions { AutoLoadProjectsMaximum = 20 });

using var provider = new LanguageServerFeatureProvider(profile, [workspaceRootDirectoryPath], serverExecutablePath);
```

## Server acquisition

The library takes a `serverExecutablePath` and stays out of acquisition, as the Lua package does with LuaLS.
The server is a .NET tool, so the machine that runs it needs the runtime the tool requires, which can be newer than the .NET 8 these packages target.
Install the tool with:

```bash
dotnet tool install --global roslyn-language-server --prerelease
```

The server that the C# extension of Visual Studio Code installs ships no Visual Basic assemblies, so it answers
C# documents only.

A `null` or whitespace-only path constructs a provider whose language features stay unavailable: it reports a
persistent startup failure instead of throwing, and every call returns its documented fallback value until the
provider is recreated with a valid path.

The `Create` factory on `LanguageServerFeatureProvider` drives a client the host already owns instead
of launching a server; ownership of the supplied
`ILanguageServerClient` transfers to the provider. The provider uses that client as supplied, so the launch
arguments, capabilities, initialization options, and settings that the profile builds from the
`RoslynLanguageServerOptions` apply only to a client the provider creates itself.

### Server runtime

Tool version 5.13.0-1.26514.10 targets `net10.0` and declares `Microsoft.NETCore.App` 10.0.0 with the roll-forward
policy `Major`, so a .NET 10 or later runtime runs it and a machine with only .NET 8 does not. The requirement can
change with the tool version, so read it for the installed version from `roslyn-language-server.runtimeconfig.json`
in the tool's folder under `~/.dotnet/tools/.store/`.

Without that runtime the server process exits at launch. The host gets `StartupFailed` with the code `StartFailed`
and no exception, and the process's exit code and the runtime's error text appear only as Warning lines of the
provider's `ILogger`, such as "exited unexpectedly" and "recent language server stderr". Read those lines first when
the server does not start.

## Options

`RoslynLanguageServerOptions` configures the session, and each member documents its default. Start from
`RoslynLanguageServerOptions.Default`.

`ShowCompletionItemsFromUnimportedNamespaces` is sent as the
`dotnet_show_completion_items_from_unimported_namespaces` setting.

The initialization options carry `AutoLoadProjectsMaximum` as the `autoLoadProjects` value, and are empty
while it is `null`. The same maximum caps the [project loading](#project-loading). The [launch arguments](#launch-arguments) record that
the server measured ignores that value.

## Launch arguments

The server is launched with `--stdio --autoLoadProjects [<n>] --telemetryLevel off`. Only the automatic
project-loading maximum varies: a `null` maximum emits the bare `--autoLoadProjects` switch (the server's own
default cap), a positive maximum appends its value, and `0` omits the switch entirely, because the server
rejects a zero maximum. An absent switch means automatic loading is off.

Measured against the server of the C# extension 2.160.4 for Visual Studio Code, the switch makes the server
discover the projects below the workspace folders and log how many it found, and a numeric value trims that
list to its size. The switch alone usually left the discovered projects unloaded, and the `autoLoadProjects`
initialization option changed neither the discovery nor the cap, so the notifications of
[project loading](#project-loading) are what load a project.

Telemetry is pinned off because a library must not enable it on the host's behalf. The profile's `Launch`
carries these arguments, so a host that adds one keeps them:
`profile with { Launch = profile.Launch with { ServerArguments = [.. profile.Launch.ServerArguments, "--logLevel", "Trace"] } }`
(an example argument).

## Project loading

A C# file in no loaded project stays a file-based program with implicit usings, where a type error such as
`CS0029` and a missing `using` report nothing, and the `--autoLoadProjects` switch alone does not reliably
load the projects it discovers. On every session start, a restart included, the profile therefore asks the
server to load them with the notifications the C# extension of Visual Studio Code sends, right after
`initialized`:

- A root that holds exactly one `.sln` or `.slnx` file at its top level sends `solution/open` for it, and the
  server opens the solution whole.
- The project files of the language (`.csproj` for C#, `.vbproj` for Visual Basic) under the other roots go
  into one `project/open`. The walk skips the `bin`, `obj` and `node_modules` folders, hidden folders and
  folders whose name starts with a dot, and it visits shallower folders first.
- `AutoLoadProjectsMaximum` caps the project files across the roots: `null` opens every one found, and `0`
  sends nothing, as it omits the switch.

The server then restores and loads the projects and reports `workspace/projectInitializationComplete`. A source
file in no loaded project stays in the server's miscellaneous-files mode.

## Settings and watch set

The settings document is keyed by the exact `workspace/configuration` section names the server asks for. The
`csharp` and `visual_basic` prefixes are always present, because the server fetches each language's value even
when a provider serves only one of them; a section the document does not carry resolves to `null`, which leaves
the server's own default in place.

The shared build inputs that affect project loading are watched recursively: the solution patterns (`*.sln` and
`*.slnx`), the project-asset file (`project.assets.json`), the build-properties, build-targets, and
build-packages files (`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`), and
`global.json`.

They are language-neutral, so the profile factory declares them once and composes a language's own source and
project patterns with them. A change to a watched input is forwarded to the server, which reloads the affected
projects.

## Diagnostics

The Roslyn language server is pull-oriented: it delivers diagnostics through `textDocument/diagnostic`, returns a
`resultId` that the client sends back as the next request's `previousResultId`, and asks the client to re-query
through `workspace/diagnostic/refresh`, rather than pushing `textDocument/publishDiagnostics`.

The framework's pull-diagnostics loop consumes those reports and feeds the same diagnostics cache and
`DiagnosticsUpdated` event the push path uses, so a host observes one model either way.

## Code actions

The Roslyn language server offers every code action with `data` and without an `edit`, and it computes the edit
when the client sends `codeAction/resolve`. The provider advertises the resolve support, keeps such an action, and
sends the resolve request only for the action the user chooses, so a quick fix such as the missing `using` of a
`List<int>` reaches the host as an action without an edit; the host's controller resolves it before it applies
the edit (see the [editor language-features guide](EditorLanguageFeaturesGuide.md#code-actions)).

The server also offers actions whose command is `roslyn.client.nestedCodeAction` or `roslyn.client.fixAllCodeAction`.
Those commands are not in the server's `executeCommandProvider`: their arguments carry nested actions and fix-all
flavors that the client interprets itself, and `workspace/executeCommand` does not run them. The provider omits an
action whose command the server does not execute, so the nested menus ("Suppress or Configure issues") and the
fix-all actions do not appear. [BACKLOG.md](https://github.com/Nickelony/LanguageServer/blob/main/BACKLOG.md) keeps
the work to interpret them.

## Workspace symbols

The Roslyn language server answers `workspace/symbol` with ranges, so it advertises no `workspaceSymbol/resolve` and
a host resolves nothing. It searches the files of the loaded projects, opened or not. Its container name is a
description such as `in Widget (project Probe (net8.0))`, not the name of the containing symbol, and an empty query
lists nothing.

## Project-load progress

The server reports the loading of the projects as work-done progress. A host that sets
`LanguageServerFeatureProviderHooks.Progress` receives the reports, because the hook makes the client advertise
`window.workDoneProgress`; without the hook the client drops them.

## Languages

A `RoslynLanguage` selects what differs between the languages, and every profile reuses
`RoslynLanguageServerOptions`, because a single server serves C# and Visual Basic and the settings document
carries a section for each:

- `RoslynLanguage.CSharp` - the `csharp` document language identifier, the `*.cs`/`*.csproj` watch
  patterns, and the `"C#"` display name.
- `RoslynLanguage.VisualBasic` - the `vb` document language identifier, the `*.vb`/`*.vbproj` watch
  patterns, and the `"Visual Basic"` display name.

## Limitations

- Loose source files with no project are loaded in the server's miscellaneous-files mode, where cross-file
  features are reduced and the server reports syntax errors and analyzer hints but no semantic compiler error: an
  unknown name (`CS0103`) or a type error shows nothing until the file belongs to a project. A host needs a
  project file beside the sources or in a folder above them, for example a `.csproj` in the root it passes to the
  provider; the server then loads the project and reports compiler errors soon after an edit. The sample writes
  such a project into its starter folder.
- The settings document models only the options whose `workspace/configuration` section name is fixed by the
  server; the remaining options keep the server's defaults.
- One profile serves one language. A workspace that holds both languages uses two providers, one per profile, and each
  starts its own server process, although the server could serve both.
- Beyond the profile, the roots and the executable path, the provider constructor takes the host hooks, the provider
  options and a logger; `RoslynLanguageServerOptions` goes to the profile factory. A process setting, such as an
  environment variable or the working directory, goes on the profile's `Launch`, and the client timeouts need a
  host-built `LanguageServerClient` passed to `Create`. The client the provider creates reports its connection
  and server-message diagnostics to the provider's logger.
- The project loading reads the roots when a session starts, so a solution or project added later is opened on
  the next start.
