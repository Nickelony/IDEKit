using System.Runtime.CompilerServices;

// Exposes internal provider seams (the Lua document store, the response-parser helpers, and the
// internal provider constructor) to the test project.
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Lua.Tests")]
