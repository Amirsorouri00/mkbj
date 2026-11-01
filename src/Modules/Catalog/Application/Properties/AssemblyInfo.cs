using System.Runtime.CompilerServices;

// The unit tests exercise identifier normalisation and GS1 check digits directly. Those are
// internal so only the aggregates construct them; without this the choice would be between
// untested arithmetic and a wider public surface than the module wants.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
