using System.Runtime.CompilerServices;

// Exposes the frozen-modifier token factory to the provider framework, which decodes the wire token stream and
// adopts its per-mask modifier lists instead of copying them. The seam is internal, so it adds no shipping surface.
[assembly: InternalsVisibleTo("Nickelony.LanguageServer.Provider")]
