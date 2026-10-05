# Nickelony.IDEKit.Processes

Run-to-completion external-process execution for compiler, formatter, linter, and generator integration,
targeting `net8.0` with nullable reference types. It depends on nothing - not even the other
`Nickelony.IDEKit` packages - so a host adopts process execution without an editor or UI-framework dependency.

## Getting started

```powershell
dotnet add package Nickelony.IDEKit.Processes --prerelease
```

Type against `IProcessRunner` so the process orchestration stays testable without launching real processes:

```csharp
using Nickelony.IDEKit.Processes;

IProcessRunner runner = new ProcessRunner();
ProcessRunResult result = await runner.RunAsync(
    new ProcessRunRequest
    {
        FileName = "dotnet",
        ArgumentList = ["format", "--verify-no-changes"],
        RedirectStandardOutput = true,
        RedirectStandardError = true
    },
    CancellationToken.None);

if (result.Succeeded)
    Console.WriteLine(result.StandardOutput);
```

## Remarks

- **Batch-scoped by design.** One request runs one process to a terminal outcome, and redirected standard output and error are drained while it runs, so output larger than the operating-system pipe buffer cannot block the run.
- **There is no standard-input piping or streaming**; an interactive or streaming process is out of scope.
- **Capture is bounded and can fail.** After the exit is observed, the capture waits a bounded grace period for the pipes to close. The text is reported as unavailable when a descendant keeps a pipe open past that period or the capture fails.
- **Capture bound.** `ProcessRunnerOptions.MaxCapturedCharactersPerStream` discards the characters past the bound while the pipe keeps being drained, and `StandardOutputTruncated` or `StandardErrorTruncated` reports that output past the bound was discarded, so the text is incomplete.
- **Cancellation is an outcome, not an exception.** An already-canceled token yields `CanceledBeforeStart` without launching anything; a token canceled while the runner waits yields `Canceled` after it attempted to terminate the process and its descendants.
- **`IProcessLauncher.Start` lets the caller drive the process.** It returns an `IProcessHandle` that the caller
  owns and disposes, and it rejects a request with `UseShellExecute` set, because a shell launch produces no handle.
  `IProcessRunner.RunAsync` and the blocking `Run` drive a request to completion, and `ProcessRunner` implements
  both interfaces.

The [processes guide](https://github.com/Nickelony/LanguageServer/blob/main/docs/ProcessesGuide.md) owns the capture behavior, the request validation, the outcome model, the handle lifetime, the options, and the cancellation contract.

## License

MIT © 2026 Kewin Kupilas.
