# Nickelony.QuickPick.Avalonia

**The Avalonia window of a quick pick**: a popup with an input over a virtualized list, placed against any control of an Avalonia host, for the [`Nickelony.QuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/src/QuickPick/Nickelony.QuickPick/README.md) controller.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.QuickPick.Avalonia.svg)](https://www.nuget.org/packages/Nickelony.QuickPick.Avalonia)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package depends on `Nickelony.QuickPick`, `Nickelony.Matching` and Avalonia and targets `net8.0`. It needs no editor control.

## Getting started

```sh
dotnet add package Nickelony.QuickPick.Avalonia --prerelease
```

```csharp
using Nickelony.QuickPick;
using Nickelony.QuickPick.Avalonia;

// commandProvider is an IQuickPickProvider, as in the Nickelony.QuickPick readme, and windowContent is the
// control of the host window that the popup is placed against.
// When the window is created, on the UI thread:
var controller = new QuickPickController(
    [new QuickPickMode(commandProvider, prefix: string.Empty)],
    options: QuickPickControllerOptions.Default with { EnableHelpMode = false });

var presenter = new QuickPickPresenter(windowContent, controller);

// In the handler of the shortcut:
controller.Open();

// When the window closes:
presenter.Dispose();
controller.Dispose();
```

## Remarks

- **The window follows the session.** It opens when the controller opens a session and closes when it ends, and disposing the presenter closes the window without ending the session.
- **A lost focus ends the session.** The session also ends when the host window deactivates, and the focus returns to the control that held it.
- **The look is a `QuickPickTheme`.** The default is a fixed light palette, and `ItemTemplate` replaces the row template.

The [quick pick guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/QuickPickGuide.md#the-window) owns the keys, the rows and the end of a session.

## License

MIT © 2026 Kewin Kupilas.
