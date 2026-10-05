# Nickelony.IDEKit.Workspace.QuickPick

**The file list of a quick open** for the [Nickelony IDEKit](https://github.com/Nickelony/LanguageServer) family. It lists the files below the workspace roots as the files mode of a `Nickelony.QuickPick` controller, keeps the list current with file watchers, and opens the file the user picks.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.IDEKit.Workspace.QuickPick.svg)](https://www.nuget.org/packages/Nickelony.IDEKit.Workspace.QuickPick)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package references `Nickelony.IDEKit.Core`, `Nickelony.IDEKit.FileWatching` and `Nickelony.QuickPick`, and not the document authority
`Nickelony.IDEKit.Workspace`, so a host takes the quick open without the document store.

## Getting started

```sh
dotnet add package Nickelony.IDEKit.Workspace.QuickPick --prerelease
```

```csharp
using Nickelony.IDEKit.Core.Paths;
using Nickelony.IDEKit.Workspace.QuickPick;
using Nickelony.QuickPick;

string workspaceRoot = Directory.GetCurrentDirectory();   // any absolute local path

using var files = new WorkspaceFileQuickPickProvider(
    [workspaceRoot],
    LocalPathComparisonPolicy.ForCurrentPlatform,
    openFileAsync: (request, cancellationToken) =>
    {
        Console.WriteLine($"{request.Path}:{request.Line}:{request.Column}");
        return Task.CompletedTask;
    },
    options: new WorkspaceFileQuickPickOptions { ExcludeGlobs = ["bin", "obj", ".git"] });

var controller = new QuickPickController([new QuickPickMode(files)]);   // create it on the UI thread

controller.Open();
```

## Remarks

- **The first query walks the roots on a pool thread.** After that, the list follows one file watcher per root, and a query never walks.
- **A host with a document store passes the store's comparison.** Pass `store.Options.PathComparison`, so the list and the documents agree on which paths are one file.
- **`FilesChanged` is raised on a pool thread.** A host marshals to its dispatcher and calls `QuickPickController.Refresh`.
- **The list is bounded.** It holds at most `WorkspaceFileQuickPickOptions.MaxFileCount` files.

The [quick pick guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/QuickPickGuide.md#files) owns the rows, the query grammar, the globs and the watcher recovery.

## License

MIT © 2026 Kewin Kupilas.
