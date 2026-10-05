# Nickelony.QuickPick.Wpf

**The WPF window of a quick pick**: a popup with an input over a virtualized list, placed against any element of a WPF host, for the [`Nickelony.QuickPick`](https://github.com/Nickelony/LanguageServer/blob/main/src/QuickPick/Nickelony.QuickPick/README.md) controller.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.QuickPick.Wpf.svg)](https://www.nuget.org/packages/Nickelony.QuickPick.Wpf)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package depends on `Nickelony.QuickPick`, `Nickelony.Matching` and WPF and targets `net8.0-windows`. It needs no editor control.

## Getting started

```sh
dotnet add package Nickelony.QuickPick.Wpf --prerelease
```

```csharp
using Nickelony.QuickPick;
using Nickelony.QuickPick.Wpf;

// commandProvider is an IQuickPickProvider, as in the Nickelony.QuickPick readme, and windowContent is the
// element of the host window that the popup is placed against.
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
- **A lost focus ends the session.** The session also ends when the host window deactivates, and the focus returns to the element that held it.
- **The controller needs the dispatcher context.** A WPF thread has it once its dispatcher runs, so a host that builds the controller earlier first installs `new DispatcherSynchronizationContext()` with `SynchronizationContext.SetSynchronizationContext`.
- **A host that no `Window` owns reports no deactivation.** Such a host, for example an `ElementHost`, ends the session by the loss of the keyboard focus alone.
- **The look is a `QuickPickTheme`.** The default takes the system brushes, and `ItemTemplate` replaces the row template.

The [quick pick guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/QuickPickGuide.md#the-window) owns the keys, the rows and the end of a session.

## License

MIT © 2026 Kewin Kupilas.
