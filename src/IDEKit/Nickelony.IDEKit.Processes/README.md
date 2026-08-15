# Nickelony.IDEKit.Processes

Run-to-completion external-process execution for compiler, formatter, linter,
and generator integration.

## Getting started

Install the package from your configured feed (the library ships from a local
feed while it is in preview):

```powershell
dotnet add package Nickelony.IDEKit.Processes
```

Type against `IProcessRunner` so the process orchestration stays testable without
launching real processes:

```csharp
using Nickelony.IDEKit.Processes;

var runner = new ProcessRunner();
ProcessRunResult result = await runner.RunAsync(
    new ProcessRunRequest
    {
        FileName = "dotnet",
        ArgumentList = ["format", "--verify-no-changes"],
        RedirectStandardOutput = true,
        RedirectStandardError = true
    },
    cancellationToken);

if (result.Outcome == ProcessRunOutcome.Exited && result.ExitCode == 0)
    Console.WriteLine(result.StandardOutput);
```

## Requirements

.NET 8.0 or later (the package targets `net8.0` and is built with nullable reference types
enabled).

## Dependencies

The package is dependency-free and takes no dependency on the other
`Nickelony.IDEKit` packages, so a host can adopt process execution without
taking an editor or UI-framework dependency.

## What's inside

The package is deliberately batch-scoped: one request runs one process to a
terminal outcome, and redirected standard output and error are drained while
the process runs, so output larger than the operating-system pipe buffer cannot
block the run. After the exit is observed, the capture waits a bounded grace
period for the pipes to close and reports the text as unavailable when a
descendant keeps them open or the capture fails - including a configured
encoding that rejects a captured byte sequence. A configured capture bound is
applied while the process runs: the excess is discarded, the pipe keeps being
drained, and the truncation is reported instead of growing the capture without
limit. The runtime's redirected pipes
are synchronous, so a quiet child keeps one thread-pool thread per redirected
stream busy while it runs. There is no standard-input piping or streaming by
design; interactive and streaming transports are out of scope for this package.

`ProcessRunRequest` is an immutable description of the launch: the executable,
file, or shell target, the raw command line (`RawArguments`) or an escaped
argument list (`ArgumentList`), the working directory, environment overrides, output
redirection with encodings, shell execution, window suppression, and an
optional timeout. The record validates single-property constraints as they are
set; the runner rejects invalid property combinations - shell execution with
redirection or environment overrides, both argument shapes set, or an encoding
without the matching redirection - before it launches anything.

- `ProcessRunOutcome` - how the run ended: `Exited`, `TimedOut`, `Canceled`,
  `CanceledBeforeStart`, or `NoProcessHandle` (shell execution can launch a
  target through the operating system without returning a process handle).
- `ProcessRunResult` - immutable outcome with the outcome value, a nullable
  exit code, the process identifier when a handle was produced, the captured
  output with its per-stream truncation flags, and the `Succeeded` convenience
  (an observed exit with code zero). The
  exit code and the output stay `null` when the exit could not be observed after
  termination was attempted; the output also stays `null` when its capture did
  not finish within the output-drain grace period or failed (a held pipe or a
  decoder that rejected the captured bytes). A truncation flag reports that the
  capture reached the configured bound, so the reported text may be incomplete.
- `IProcessRunner` / `ProcessRunner` - `RunAsync` (and the blocking `Run`)
  drive a request to completion; `Start` returns an `IProcessHandle` for
  callers that drive the process lifetime themselves. `Start` requires a
  handle-producing request, so it rejects shell execution (which reports
  `NoProcessHandle` through `Run`/`RunAsync` instead).
- `ProcessRunnerOptions` - the termination-confirmation and output-drain grace
  periods (five seconds each by default) and the per-stream capture bound
  (`MaxCapturedCharactersPerStream`, unbounded by default). Shorten a period to
  keep a tool integration responsive, at the cost of reporting more exits as
  unobserved and more output as unavailable; set the bound so a flooding tool
  cannot exhaust memory while it runs.
- `IProcessHandle` - waiting (blocking and asynchronous), killing, process-tree
  termination, the process identifier (`ProcessId`), and the drained standard
  output/error for caller-driven process lifetimes, each with a flag that
  reports whether the stream reached the capture bound. Disposing the handle cancels
  pending captures instead of letting them buffer output after the run, and the
  blocking capture accessors can wait for as long as a writer keeps the stream
  open. Disposal is idempotent and safe to race with the members: a member that
  runs concurrently with disposal either completes or throws
  `ObjectDisposedException` when it reaches the released process, while any other
  failure the race produces keeps its own type so a genuine fault is not hidden
  behind the disposal.

Cancellation is reported as an outcome instead of an exception: a token that is
already canceled yields `CanceledBeforeStart` without launching anything, and a
token canceled while the runner waits yields `Canceled` after the runner
attempted to terminate the process and its descendants. After a timeout or
cancellation, termination confirmation and output capture can add the
configured grace periods before the call returns.

Hosts keep their own workflows (which tools to run, how to stage inputs, and how
to interpret outputs and exit codes) and type them against `IProcessRunner`.

## License

MIT © 2026 Kewin Kupilas.
