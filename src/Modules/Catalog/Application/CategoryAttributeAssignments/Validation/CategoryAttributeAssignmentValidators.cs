using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Validation;

public sealed class CategoryAttributeAssignmentValidator : AbstractValidator<(long CategoryId, long AttributeBlueprintId, int? TitleOrder)>
{
    public CategoryAttributeAssignmentValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category ID must be positive.");
        RuleFor(x => x.AttributeBlueprintId).GreaterThan(0).WithMessage("Attribute blueprint ID must be positive.");
        RuleFor(x => x.TitleOrder).GreaterThanOrEqualTo(0).When(x => x.TitleOrder.HasValue).WithMessage("Title order cannot be negative.");
    }
}

public sealed class ReorderCategoryAttributeValidator : AbstractValidator<(long AssignmentId, int TitleOrder)>
{
    public ReorderCategoryAttributeValidator()
    {
        RuleFor(x => x.AssignmentId).GreaterThan(0).WithMessage("Assignment ID must be positive.");
        RuleFor(x => x.TitleOrder).GreaterThanOrEqualTo(0).WithMessage("Title order cannot be negative.");
    }
}
