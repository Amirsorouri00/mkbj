using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.Products.Validation;

public sealed class CreateProductValidator : AbstractValidator<(string Sku, string Name, decimal Price, string Currency, int InitialStock, string? Description)>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().WithMessage("SKU is required.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200).WithMessage("Name is required and must not exceed 200 characters.");
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be greater than zero.");
        RuleFor(x => x.Currency).Length(3).WithMessage("Currency must be a 3-letter ISO code.");
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0).WithMessage("Initial stock cannot be negative.");
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null).WithMessage("Description must not exceed 2000 characters.");
    }
}
