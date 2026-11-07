using System.Runtime.CompilerServices;

// The gateways are internal; the mapping tests construct them against the real provider types,
// which is the only place a field-by-field mapping can actually be shown to be right.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
