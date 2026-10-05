# Shared source

`shared/` holds the product source that more than one project compiles, so each body exists once. A project links
a home into its build with a `<Compile>` item, and nothing here builds on its own. Test source that several test
projects compile lives under `tests/`, in its family's folder. The
[editor sharing guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/EditorSharingGuide.md) says when a
piece belongs in a home and how a shared file is shaped, and the
[architecture guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ArchitectureGuide.md#one-home-for-a-rule)
gives the reason.

## Homes

Each folder below is one home. A file that differs by engine or family reads the define of its row in an `#if`
header, which selects the toolkit `using`, the namespace and, for `Primitives/`, the visibility.

| Home | Compiled by | Define |
| --- | --- | --- |
| `Editor/` | `Nickelony.IDEKit.AvalonEdit`, `Nickelony.IDEKit.AvaloniaEdit` | `AVALONIAEDIT` |
| `Editor.LanguageFeatures/` | `Nickelony.IDEKit.AvalonEdit.LanguageFeatures`, `Nickelony.IDEKit.AvaloniaEdit.LanguageFeatures` | `AVALONIAEDIT` |
| `Editor.Markdown/` | `Nickelony.IDEKit.AvalonEdit.Markdown`, `Nickelony.IDEKit.AvaloniaEdit.Markdown` | `AVALONIAEDIT` |
| `Editor.TextMate/` | `Nickelony.IDEKit.AvalonEdit.TextMate`, `Nickelony.IDEKit.AvaloniaEdit.TextMate` | `AVALONIAEDIT` |
| `Infrastructure/` | `Nickelony.IDEKit.Core`, `Nickelony.LanguageServer.Client` | none |
| `KeyBindings/` | `Nickelony.KeyBindings.Wpf`, `Nickelony.KeyBindings.Avalonia` | `AVALONIAKEYBINDINGS` |
| `Primitives/` | `Nickelony.IDEKit.Core`, `Nickelony.QuickPick` | `QUICKPICKFAMILY` |
| `QuickPick/` | `Nickelony.QuickPick.Wpf`, `Nickelony.QuickPick.Avalonia` | `AVALONIAQUICKPICK` |
| `Samples/` | `Nickelony.Samples.Wpf`, `Nickelony.Samples.Avalonia` | `AVALONIAEDIT` |
| `docs/` | no project | none |

`docs/` holds XML documentation fragments. An `<include>` element in a project's XML comments pulls a fragment in,
so the prose that two bindings repeat has one home. No project compiles the folder.

## What the defines select

- **`AVALONIAEDIT`** is derived. A project states `<EditorEngine>Avalonia</EditorEngine>`, and
  `Directory.Build.targets` defines the symbol for it. A project that states `Wpf` compiles the `#else` branch.
- **`AVALONIAKEYBINDINGS`** and **`AVALONIAQUICKPICK`** are defined by the project file of
  `Nickelony.KeyBindings.Avalonia` and of `Nickelony.QuickPick.Avalonia`. Their families state no editor engine,
  because they reference no editor.
- **`QUICKPICKFAMILY`** is defined by the project file of `Nickelony.QuickPick`, which references no Core package.
  With it, a file of `Primitives/` declares the flat `Nickelony.QuickPick` namespace and its types are `internal`.
  Without it, in Core, the types are public and sit in the slice namespace that their folder spells.
- **`Infrastructure/`** needs no define. Its helpers are `internal` and declare the one flat
  `Nickelony.Infrastructure` namespace in every package that compiles them.
