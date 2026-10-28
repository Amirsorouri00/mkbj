using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using FluentValidation;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Validation;

public sealed class CreateAttributeBlueprintValidator
    : AbstractValidator<(string Name, AttributeBlueprintDataType DataType)>
{
    public CreateAttributeBlueprintValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .WithMessage("Attribute blueprint name is required.")
            .MaximumLength(200)
            .WithMessage("Name is required and must not exceed 200 characters.");

        RuleFor(request => request.DataType)
            .IsInEnum()
            .WithMessage("Attribute blueprint data type is invalid.");
    }
}

public sealed class RenameAttributeBlueprintValidator
    : AbstractValidator<(long Id, string Name)>
{
    public RenameAttributeBlueprintValidator()
    {
        RuleFor(request => request.Id)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint ID must be positive.");

        RuleFor(request => request.Name)
            .NotEmpty()
            .WithMessage("Attribute blueprint name is required.")
            .MaximumLength(200)
            .WithMessage("Name is required and must not exceed 200 characters.");
    }
}

public sealed class ChangeAttributeBlueprintDataTypeValidator
    : AbstractValidator<(long Id, AttributeBlueprintDataType DataType)>
{
    public ChangeAttributeBlueprintDataTypeValidator()
    {
        RuleFor(request => request.Id)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint ID must be positive.");

        RuleFor(request => request.DataType)
            .IsInEnum()
            .WithMessage("Attribute blueprint data type is invalid.");
    }
}

public sealed class AttributeBlueprintIdValidator : AbstractValidator<long>
{
    public AttributeBlueprintIdValidator()
    {
        RuleFor(id => id)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint ID must be positive.");
    }
}

public sealed class AttributeBlueprintOptionInputValidator
    : AbstractValidator<(string Value, long? MeasurementUnitId)>
{
    public AttributeBlueprintOptionInputValidator()
    {
        RuleFor(request => request.Value)
            .NotEmpty()
            .WithMessage("Attribute blueprint option value is required.")
            .MaximumLength(1000)
            .WithMessage("Attribute blueprint option value cannot exceed 1000 characters.");

        RuleFor(request => request.MeasurementUnitId)
            .GreaterThan(0)
            .When(request => request.MeasurementUnitId.HasValue)
            .WithMessage("Measurement unit ID must be positive.");
    }
}

public sealed class AttributeBlueprintOptionIdsValidator
    : AbstractValidator<(long AttributeBlueprintId, long OptionId)>
{
    public AttributeBlueprintOptionIdsValidator()
    {
        RuleFor(request => request.AttributeBlueprintId)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint ID must be positive.");

        RuleFor(request => request.OptionId)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint option ID must be positive.");
    }
}

public sealed class UpdateAttributeBlueprintValidator
    : AbstractValidator<(long Id, string Name, AttributeBlueprintDataType DataType,
        IReadOnlyCollection<UpdateAttributeBlueprintOptionInput> Options)>
{
    public UpdateAttributeBlueprintValidator()
    {
        RuleFor(request => request.Id)
            .GreaterThan(0)
            .WithMessage("Attribute blueprint ID must be positive.");

        RuleFor(request => request.Name)
            .NotEmpty()
            .WithMessage("Attribute blueprint name is required.")
            .MaximumLength(200)
            .WithMessage("Name is required and must not exceed 200 characters.");

        RuleFor(request => request.DataType)
            .IsInEnum()
            .WithMessage("Attribute blueprint data type is invalid.");

        RuleFor(request => request.Options)
            .NotNull()
            .WithMessage("Attribute blueprint options are required.")
            .Must(HaveUniqueOptionIds)
            .WithMessage("Attribute blueprint option IDs must be unique.");

        RuleForEach(request => request.Options).ChildRules(option =>
        {
            option.RuleFor(value => value.OptionId)
                .GreaterThan(0)
                .When(value => value.OptionId.HasValue)
                .WithMessage("Attribute blueprint option ID must be positive.");

            option.RuleFor(value => value.Value)
                .NotEmpty()
                .WithMessage("Attribute blueprint option value is required.")
                .MaximumLength(1000)
                .WithMessage("Attribute blueprint option value cannot exceed 1000 characters.");

            option.RuleFor(value => value.MeasurementUnitId)
                .GreaterThan(0)
                .When(value => value.MeasurementUnitId.HasValue)
                .WithMessage("Measurement unit ID must be positive.");
        });
    }

    private static bool HaveUniqueOptionIds(IReadOnlyCollection<UpdateAttributeBlueprintOptionInput>? options)
    {
        if (options is null) return true;

        var optionIds = options
            .Where(option => option.OptionId.HasValue)
            .Select(option => option.OptionId!.Value)
            .ToArray();

        return optionIds.Length == optionIds.Distinct().Count();
    }
}
