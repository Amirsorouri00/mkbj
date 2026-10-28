using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;

public sealed record AttributeBlueprintCreatedDomainEvent(long AttributeBlueprintId, string Name, AttributeBlueprintDataType DataType) : DomainEvent;
public sealed record AttributeBlueprintRenamedDomainEvent(long AttributeBlueprintId, string Name) : DomainEvent;
public sealed record AttributeBlueprintDataTypeChangedDomainEvent(long AttributeBlueprintId, AttributeBlueprintDataType PreviousDataType, AttributeBlueprintDataType NewDataType) : DomainEvent;
public sealed record AttributeBlueprintActivatedDomainEvent(long AttributeBlueprintId) : DomainEvent;
public sealed record AttributeBlueprintDeactivatedDomainEvent(long AttributeBlueprintId) : DomainEvent;
public sealed record AttributeBlueprintDeletedDomainEvent(long AttributeBlueprintId) : DomainEvent;
public sealed record AttributeBlueprintArchivedDomainEvent(long AttributeBlueprintId) : DomainEvent;
public sealed record AttributeBlueprintUnarchivedDomainEvent(long AttributeBlueprintId, AttributeBlueprintStatus Status) : DomainEvent;
public sealed record AttributeBlueprintOptionAddedDomainEvent(long AttributeBlueprintId, long OptionId, string Value, long? MeasurementUnitId) : DomainEvent;
public sealed record AttributeBlueprintOptionUpdatedDomainEvent(long AttributeBlueprintId, long OptionId, string Value, long? MeasurementUnitId) : DomainEvent;
public sealed record AttributeBlueprintOptionRemovedDomainEvent(long AttributeBlueprintId, long OptionId) : DomainEvent;

