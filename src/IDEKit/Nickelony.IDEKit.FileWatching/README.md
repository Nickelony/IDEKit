# Nickelony.IDEKit.FileWatching

**Debounced file-system watching with snapshot-based recovery** for the [Nickelony IDEKit](https://github.com/Nickelony/LanguageServer) family. It watches the files of a workspace root, coalesces bursts of changes into batches, and reconciles the changes missed while a watcher was unavailable.

[![NuGet](https://img.shields.io/nuget/v/Nickelony.IDEKit.FileWatching.svg)](https://www.nuget.org/packages/Nickelony.IDEKit.FileWatching)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/Nickelony/LanguageServer/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

The package references only `Nickelony.IDEKit.Core`, for the local-path comparison, so a host takes it without the editor packages. The language-server
provider framework builds its workspace watching on it, and a host that owns its own watcher glue can use it directly.

## Getting started

```sh
dotnet add package Nickelony.IDEKit.FileWatching --prerelease
```

```csharp
using Nickelony.IDEKit.FileWatching;

string workspaceRootDirectoryPath = Directory.GetCurrentDirectory();   // any absolute local path
FileWatchSpecification[] specifications = [new("*.cs", IncludeSubdirectories: true)];

using var watcher = new FileWatcher(
    workspaceRootDirectoryPath,
    dispatchAsync: (batch, cancellationToken) =>
    {
        foreach (FileChange change in batch.Entries)
            Console.WriteLine($"{change.Kind}: {change.Path}");

        return Task.CompletedTask;
    },
    specifications);

FileWatcherStartResult result = watcher.Start();

if (!result.Succeeded)
    Console.WriteLine($"The watcher did not start: {result.Outcome}.");
```

A bare `FileWatcher` stops for good after an engine error unless the host passes `onWatcherFailed` and replaces it. A `FileWatchSupervisor` owns the
watcher and the `FileSnapshotTracker` of one root: when the watcher fails, it starts a replacement and dispatches the changes that the tracked
snapshot shows were missed, so a host that watches for the life of a window takes the supervisor.

## Remarks

- **Bursts coalesce.** Changes close together form one batch, and a sustained stream still dispatches periodically.
- **A failed dispatch retries with backoff.** After the attempt limit the batch is dropped and the owner is told once, so it can replace the watcher.
- **Patterns are file-name filters.** A filter holds no directory separator, and the casing rule follows the `LocalPathComparisonPolicy` that the host passes.

The [file watching guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/FileWatchingGuide.md) owns the lifecycle, the recovery model and the limitations.

## License

MIT © 2026 Kewin Kupilas.