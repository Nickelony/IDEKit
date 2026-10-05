# Nickelony IDE and language tooling

Documentation for the `Nickelony.IDEKit.*`, `Nickelony.LanguageServer.*`, `Nickelony.KeyBindings.*`, and
`Nickelony.QuickPick.*` package families, and for `Nickelony.Matching`:

- `Nickelony.IDEKit.*` supplies the editor-neutral text and language-feature contracts, document authority,
  and the WPF and Avalonia editor bindings.
- `Nickelony.LanguageServer.*` supplies an LSP client, a provider framework, and ready language packages that
  implement the language-feature contracts over a real language server.
- `Nickelony.KeyBindings.*` supplies a toolkit-neutral command and keyboard-shortcut system, with WPF and
  Avalonia adapters, for any application with a command surface.
- `Nickelony.QuickPick.*` supplies a toolkit-neutral quick pick for a command palette or a quick open, with WPF and
  Avalonia windows, for any application. `Nickelony.Matching` supplies the fuzzy matching that it ranks with.

The guides cover the model, the lifecycles, and the deliberate trade-offs. The API reference is generated from
the XML documentation in the source, so a member's parameters, returns, exceptions, and remarks appear on its
page. The README of a package that a host uses directly gives its install command and a runnable example.

## Start here

- To show language features from a ready language package, read the
  [consumer integration guide](ConsumerIntegration.md) and the
  [Lua package README](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Lua/README.md).
- To install the LuaLS executable that the Lua package launches, read the
  [Lua guide](LuaGuide.md).
- To install the Roslyn language server that the Roslyn package launches for C# and Visual Basic, read the
  [Roslyn guide](RoslynGuide.md).
- To host an editor control, read the [editor usage guide](EditorUsageGuide.md) and the
  [AvalonEdit](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/AvalonEdit/Nickelony.IDEKit.AvalonEdit/README.md)
  or
  [AvaloniaEdit](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/AvaloniaEdit/Nickelony.IDEKit.AvaloniaEdit/README.md)
  package README.
- To implement a provider for a new language, read the [provider authoring guide](ProviderAuthoring.md) and
  the [Provider package README](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Provider/README.md).
- To add commands and keyboard shortcuts, read the [key bindings guide](KeyBindingsGuide.md) and the
  [KeyBindings package README](https://github.com/Nickelony/LanguageServer/blob/main/src/KeyBindings/Nickelony.KeyBindings/README.md).
- To add a command palette, quick open or another quick pick, read the [quick pick guide](QuickPickGuide.md).
- To rank a list of names by a typed query, read the [matching guide](MatchingGuide.md).
- To track documents and their files without an editor, read the [workspace guide](WorkspaceGuide.md) and the
  [Workspace package README](https://github.com/Nickelony/LanguageServer/blob/main/src/IDEKit/Nickelony.IDEKit.Workspace/README.md).
- To speak LSP to a server directly, read the [client guide](ClientGuide.md) and the
  [Client package README](https://github.com/Nickelony/LanguageServer/blob/main/src/LanguageServer/Nickelony.LanguageServer.Client/README.md).
- To see how the packages relate before you add a project, a reference or a namespace, read the
  [architecture guide](ArchitectureGuide.md).

## Reference

- The [core guide](CoreGuide.md) covers the editor-neutral text model, comment and string scanning, the
  text-edit kernel, request coordination, and the behavior rules of auto-closing, find and replace, line
  diffing, indentation, and bookmarks.
- The [language features guide](LanguageFeaturesGuide.md) covers the editor-agnostic language-feature
  contracts and payloads.
- The [editor language-features guide](EditorLanguageFeaturesGuide.md) covers completion, hover, signature help, code
  actions, and navigation in a binding.
- The [editor binding guide](EditorBindingGuide.md) covers the structure of an editor binding and how to add one.
- The [editor sharing guide](EditorSharingGuide.md) covers how the two bindings share one copy of each file, test, and
  documentation block.
- The [Markdown guide](MarkdownGuide.md) covers Markdown rendering in the editor bindings.
- The [TextMate guide](TextMateGuide.md) covers TextMate grammar highlighting in the editor bindings.
- The [file watching guide](FileWatchingGuide.md) covers debounced file watching with snapshot-based recovery.
- The [processes guide](ProcessesGuide.md) covers running an external process to completion.
- The [workspace views guide](WorkspaceViewsGuide.md) covers the document-view contract and the view-aware document
  coordinator.
- The [provider guide](ProviderGuide.md) covers the public surface and conventions of the Provider package.
- The [language server internals](LanguageServerInternals.md) covers the connection and provider machinery behind the Client
  and Provider packages.
- The [Roslyn design note](RoslynProviderDesign.md) explains why C# and Visual Basic share one package.
- The [key binding context design](KeyBindingContextDesign.md) explains the design of context-scoped key bindings.
- The [documentation style guide](DocStyleGuide.md) shows the documentation rules as before-and-after pairs.
