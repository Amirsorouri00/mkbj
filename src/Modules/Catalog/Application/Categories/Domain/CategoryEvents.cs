using EwaysVNext.Domain.Events;

namespace EwaysVNext.Modules.Catalog.Application.Categories.Domain;

public sealed record CategoryCreatedDomainEvent(string Name, string Slug) : DomainEvent;
public sealed record CategoryUpdatedDomainEvent(long CategoryId, string Name, string Slug) : DomainEvent;
public sealed record CategoryMovedDomainEvent(long CategoryId, long? ParentId, int DisplayOrder) : DomainEvent;
public sealed record CategoryActivatedDomainEvent(long CategoryId) : DomainEvent;
public sealed record CategoryDeactivatedDomainEvent(long CategoryId) : DomainEvent;
public sealed record CategoryDeletedDomainEvent(long CategoryId, DateTimeOffset DeletedAt) : DomainEvent;
