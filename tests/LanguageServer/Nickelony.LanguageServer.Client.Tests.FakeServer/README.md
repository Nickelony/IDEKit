# Client test fake server

A stdio language server for the real-process tests in `Nickelony.LanguageServer.Client.Tests`. The client
starts it as `dotnet <fake server dll> <arguments>`, so the tests run process start, framing, and stderr
capture on real pipes.

It answers:

- `initialize`, with the `capabilities` given by `--capabilities <json>` (default `{}`).
- `shutdown`, with a `null` result; `exit` ends the process.
- `example/echo`, with `{"echoed": <params>}`.
- Any other request, with a `-32601` error; notifications get no reply.

`--exit-immediately` exits before the handshake; every start writes `fake-server stderr marker` to stderr.

The test project references this project with `ReferenceOutputAssembly="false"`, and a test finds the DLL
under the `FakeServerOutputDirectory` metadata folder. The test
`SendRequestAsync_BeyondTheInitializeHandshake_RoundTripsThroughTheStdioTransport` in
`LanguageServerClientRealProcessTests.cs` starts it with `--capabilities` and asserts the echoed `value`.
