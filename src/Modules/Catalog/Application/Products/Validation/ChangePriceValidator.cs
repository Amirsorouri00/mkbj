using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.Products.Validation;

public sealed class ChangePriceValidator : AbstractValidator<(long ProductId, decimal NewPrice, string Currency)>
{
    public ChangePriceValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required.");
        RuleFor(x => x.NewPrice).GreaterThan(0).WithMessage("Price must be greater than zero.");
        RuleFor(x => x.Currency).Length(3).WithMessage("Currency must be a 3-letter ISO code.");
    }
}
