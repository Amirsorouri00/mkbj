using System.Runtime.CompilerServices;

// The composition internals — the PRNG, the draft line shape and the exact-hit assignment — are
// internal because nothing outside the engine may call them, and tested directly because they are
// where the arithmetic that has to be exact lives. Widening them to public for the tests' benefit
// would make them part of the module's surface.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
