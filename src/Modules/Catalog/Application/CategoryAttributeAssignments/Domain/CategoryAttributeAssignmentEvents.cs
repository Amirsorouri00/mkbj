using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;

public sealed record CategoryAttributeAssignedDomainEvent(long AssignmentId, long CategoryId, long AttributeBlueprintId) : DomainEvent;

public sealed record CategoryAttributeBecameRequiredDomainEvent(long AssignmentId, long CategoryId, long AttributeBlueprintId) : DomainEvent;

public sealed record CategoryAttributeFlagsChangedDomainEvent(long AssignmentId, long CategoryId, long AttributeBlueprintId) : DomainEvent;

public sealed record CategoryAttributeTitleConfigurationChangedDomainEvent(
    long AssignmentId, long CategoryId, long AttributeBlueprintId, bool IsInTitle, int? TitleOrder) : DomainEvent;

public sealed record CategoryAttributePriorityChangedDomainEvent(
    long AssignmentId, long CategoryId, long AttributeBlueprintId, bool IsPrimary, bool IsVariantGenerating) : DomainEvent;

public sealed record CategoryAttributeUnassignedDomainEvent(long AssignmentId, long CategoryId, long AttributeBlueprintId) : DomainEvent;
