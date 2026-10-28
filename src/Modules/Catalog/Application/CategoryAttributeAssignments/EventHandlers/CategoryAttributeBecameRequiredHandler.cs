using EwaysVNext.Events.Application;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.EventHandlers;

public sealed class CategoryAttributeBecameRequiredHandler(
    ILogger<CategoryAttributeBecameRequiredHandler> logger)
    : IDomainEventHandler<CategoryAttributeBecameRequiredDomainEvent>
{
    public Task HandleAsync(CategoryAttributeBecameRequiredDomainEvent @event, CancellationToken ct = default)
    {
        logger.LogWarning(
            "CategoryAttributeAssignment {AssignmentId}: attribute {AttributeBlueprintId} became required for category {CategoryId}. " +
            "Retroactive Incomplete Draft marking is not yet implemented: Product has no category or attribute-value model to check against.",
            @event.AssignmentId, @event.AttributeBlueprintId, @event.CategoryId);

        return Task.CompletedTask;
    }
}
