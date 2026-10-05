namespace Nickelony.LanguageServer.Provider.Tests;

/// <summary>Non-nullable struct payload that pins the request helper's outcome-based fallback classification.</summary>
/// <param name="Value">The payload value mapped by the test parser.</param>
internal readonly record struct StubStructResponse(int Value);
