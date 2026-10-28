using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;

public sealed record CategoryAttributeExcludedDomainEvent(long ExclusionId, long CategoryId, long SourceAssignmentId) : DomainEvent;
