# Nickelony.QuickPick

**A toolkit-neutral quick pick** for a command palette or a quick open: the controller that picks a mode by the prefix of the typed text, queries its provider, filters, ranks and previews the items, and accepts one.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.QuickPick.svg)](https://www.nuget.org/packages/Nickelony.QuickPick)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package references only `Nickelony.Matching` and draws nothing, so a host with no editor, such as a 3D view, takes a command palette with `Nickelony.KeyBindings` and a window package: `Nickelony.QuickPick.Wpf` or `Nickelony.QuickPick.Avalonia`.

## Getting started

```sh
dotnet add package Nickelony.QuickPick --prerelease
```

A provider supplies the items of one mode and acts on the one the user accepts:

```csharp
using Nickelony.QuickPick;

internal sealed class CommandProvider(IReadOnlyList<string> names, Action<string> run) : IQuickPickProvider
{
    public string Prefix => string.Empty;
    public string Name => "Commands";
    public string Placeholder => "Type a command";

    public Task<IReadOnlyList<QuickPickItem>> GetItemsAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QuickPickItem>>([.. names.Select(name => new QuickPickItem(name, name))]);

    public Task AcceptAsync(QuickPickAcceptRequest request, CancellationToken cancellationToken = default)
    {
        run(request.Item.Key);
        return Task.CompletedTask;
    }
}
```

A host creates the controller on its UI thread, with one mode under the empty prefix, and opens it from a shortcut:

```csharp
using Nickelony.QuickPick;

string[] names = ["Format Document", "Toggle Line Comment", "Go to Line"];

// `?` filters as plain text, because the help mode is off.
var controller = new QuickPickController(
    [new QuickPickMode(new CommandProvider(names, name => Console.WriteLine($"Run {name}")))],
    options: QuickPickControllerOptions.Default with { EnableHelpMode = false });

controller.Open();
```

## Remarks

- **A host offers the modes it wants.** Each `QuickPickMode` pairs a provider with a prefix, or with the `Prefix` of the provider when it gives none, and the help mode decides whether `?` lists the modes.
- **The controller presents nothing.** A window package shows its `State`, and a host with its own window reads `State` when `Changed` is raised.
- **One thread owns the controller.** The thread that creates it is the one that calls it, and the `SynchronizationContext.Current` of that thread carries the timers and the answers of the providers back to it.
- **A UI thread has a context**, and a console host installs a pump first, because the constructor refuses a thread with no context or with only the base `SynchronizationContext` type.
- **Only `Dispose` may come from another thread**, and it posts its teardown to the owner.

The [quick pick guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/QuickPickGuide.md) owns the provider contract, the filter modes, the recent items and the modes of the IDEKit packages.

## License

MIT © 2026 Kewin Kupilas.
