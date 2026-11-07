using System.Runtime.CompilerServices;

// The services are internal — a caller outside the module holds the port, not the class — and
// the unit tests construct them directly to exercise the composition rules with fake ports.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
