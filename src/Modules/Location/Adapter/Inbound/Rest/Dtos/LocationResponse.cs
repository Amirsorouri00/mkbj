namespace EwaysVNext.Modules.MetaData.Adapter.Inbound.Rest.Dtos;

public sealed record LocationResponse(int id, string name, bool haveChild = true);
