using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.Products.Validation;

public sealed class ReserveStockValidator : AbstractValidator<(long ProductId, int Quantity)>
{
    public ReserveStockValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Product ID is required.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be greater than zero.");
    }
}
