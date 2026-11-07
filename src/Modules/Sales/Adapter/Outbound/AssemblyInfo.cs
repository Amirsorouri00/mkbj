using System.Runtime.CompilerServices;

// The gateways and the candidate cache are internal: nothing outside this assembly may construct
// them, since they are reached through their ports. They are tested directly because the cache's
// reuse rules — version match and TTL — are where a withdrawn product would be sold, and that is
// worth testing without a database and a host behind it.
[assembly: InternalsVisibleTo("EwaysVNext.UnitTests")]
