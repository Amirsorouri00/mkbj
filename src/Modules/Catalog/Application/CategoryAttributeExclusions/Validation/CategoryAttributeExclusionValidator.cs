using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Validation;

public sealed class CategoryAttributeExclusionValidator : AbstractValidator<(long CategoryId, long SourceAssignmentId)>
{
    public CategoryAttributeExclusionValidator()
    {
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("Category ID must be positive.");
        RuleFor(x => x.SourceAssignmentId).GreaterThan(0).WithMessage("Source assignment ID must be positive.");
    }
}
