using Mkbj.Domain.Events;

namespace Mkbj.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;

public sealed record CategoryAttributeExcludedDomainEvent(long ExclusionId, long CategoryId, long SourceAssignmentId) : DomainEvent;
