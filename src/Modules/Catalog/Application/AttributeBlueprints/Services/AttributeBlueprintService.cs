using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Validation;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;

public sealed class AttributeBlueprintService(
    IAttributeBlueprintRepository repository,
    IMeasurementUnitRepository measurementUnitRepository,
    IUnitOfWork unitOfWork,
    IAttributeBlueprintDependencyGateway dependencyGateway,
    IClock clock,
    IIdGenerator idGenerator,
    CreateAttributeBlueprintValidator createValidator,
    RenameAttributeBlueprintValidator renameValidator,
    ChangeAttributeBlueprintDataTypeValidator changeDataTypeValidator,
    AttributeBlueprintIdValidator idValidator,
    AttributeBlueprintOptionInputValidator optionInputValidator,
    AttributeBlueprintOptionIdsValidator optionIdsValidator,
    UpdateAttributeBlueprintValidator updateValidator) : IAttributeBlueprintService
{
    public async Task<Result<long>> CreateAsync(string name, AttributeBlueprintDataType dataType,
        IReadOnlyCollection<AttributeBlueprintOptionInput> options, CancellationToken ct = default)
    {
        var validation = await createValidator.ValidateAsync((name, dataType), ct);
        if (!validation.IsValid)
        {
            return Result.Failure<long>(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        if (options is null)
        {
            return Result.Failure<long>(ValidationError(["Attribute blueprint options are required."]));
        }

        foreach (var option in options)
        {
            var optionValidation = await optionInputValidator.ValidateAsync((option.Value, option.MeasurementUnitId), ct);
            if (!optionValidation.IsValid)
            {
                return Result.Failure<long>(ValidationError(optionValidation.Errors.Select(error => error.ErrorMessage)));
            }
        }

        AttributeBlueprint blueprint;
        try
        {
            var now = clock.UtcNow;
            blueprint = AttributeBlueprint.Create(idGenerator.Generate(), name, dataType, now);
            foreach (var option in options)
            {
                blueprint.AddOption(idGenerator.Generate(), option.Value, option.MeasurementUnitId, now);
            }
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(new Error(exception.Code, exception.Message));
        }

        if (await repository.NameExistsAsync(blueprint.Name, ct: ct))
        {
            return Result.Failure<long>(NameTaken(blueprint.Name));
        }

        var measurementUnitError = await FindMissingMeasurementUnitAsync(
            blueprint.DataType,
            blueprint.Options.Select(option => option.MeasurementUnitId),
            ct);
        if (measurementUnitError is not null)
        {
            return Result.Failure<long>(measurementUnitError);
        }

        await repository.AddAsync(blueprint, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(blueprint.Id);
    }

    public async Task<Result> UpdateAsync(long id, string name, AttributeBlueprintDataType dataType, bool isActive,
        IReadOnlyCollection<UpdateAttributeBlueprintOptionInput> options, CancellationToken ct = default)
    {
        var validation = await updateValidator.ValidateAsync((id, name, dataType, options), ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();
        if (blueprint.Status == AttributeBlueprintStatus.Archived) return Result.Failure(Archived());

        var existingOptions = blueprint.Options.ToDictionary(option => option.Id);
        if (options.Any(option => option.OptionId is { } optionId && !existingOptions.ContainsKey(optionId)))
        {
            return OptionNotFound();
        }

        var updatedOptions = options.Select(option => (
            Id: option.OptionId ?? idGenerator.Generate(),
            option.Value,
            option.MeasurementUnitId)).ToArray();
        try { blueprint.ValidateDetailsUpdate(name, dataType, updatedOptions); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        var normalizedName = name.Trim();
        if (!string.Equals(blueprint.Name, normalizedName, StringComparison.Ordinal) &&
            await repository.NameExistsAsync(normalizedName, id, ct))
        {
            return Result.Failure(NameTaken(normalizedName));
        }

        var dataTypeChanged = blueprint.DataType != dataType;
        if (dataTypeChanged)
        {
            var dependencies = await dependencyGateway.GetDependenciesAsync(id, ct);
            if (dependencies.ProductValueCount > 0)
            {
                return Result.Failure(DataTypeInUse());
            }
        }

        var measurementUnitError = await FindMissingMeasurementUnitAsync(
            dataType,
            options.Select(option => option.MeasurementUnitId),
            ct);
        if (measurementUnitError is not null)
        {
            return Result.Failure(measurementUnitError);
        }

        var requestedOptionIds = options
            .Where(option => option.OptionId.HasValue)
            .Select(option => option.OptionId!.Value)
            .ToHashSet();
        var removedOptions = blueprint.Options.Where(option => !requestedOptionIds.Contains(option.Id)).ToArray();
        var productValueOptionIds = new List<long>();
        if (!dataTypeChanged)
        {
            foreach (var option in removedOptions)
            {
                if (await dependencyGateway.CountProductValuesForOptionAsync(id, option.Id, ct) > 0)
                {
                    productValueOptionIds.Add(option.Id);
                }
            }
        }

        try
        {
            blueprint.UpdateDetails(name, dataType, isActive, updatedOptions, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error(exception.Code, exception.Message));
        }

        foreach (var optionId in productValueOptionIds)
        {
            await dependencyGateway.RemoveProductValuesForOptionAsync(id, optionId, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RenameAsync(long id, string name, CancellationToken ct = default)
    {
        var validation = await renameValidator.ValidateAsync((id, name), ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        var normalizedName = name.Trim();
        if (string.Equals(blueprint.Name, normalizedName, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        if (await repository.NameExistsAsync(normalizedName, id, ct))
        {
            return Result.Failure(NameTaken(normalizedName));
        }

        try { blueprint.Rename(name, clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ChangeDataTypeAsync(long id, AttributeBlueprintDataType dataType, CancellationToken ct = default)
    {
        var validation = await changeDataTypeValidator.ValidateAsync((id, dataType), ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        if (blueprint.DataType == dataType) return Result.Success();

        if (blueprint.Options.Count > 0)
        {
            return Result.Failure(DataTypeHasOptions());
        }

        var dependencies = await dependencyGateway.GetDependenciesAsync(id, ct);
        if (dependencies.ProductValueCount > 0)
        {
            return Result.Failure(DataTypeInUse());
        }

        try { blueprint.ChangeDataType(dataType, clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<long>> AddOptionAsync(long attributeBlueprintId, string value, long? measurementUnitId,
        CancellationToken ct = default)
    {
        var idValidation = await idValidator.ValidateAsync(attributeBlueprintId, ct);
        if (!idValidation.IsValid)
        {
            return Result.Failure<long>(ValidationError(idValidation.Errors.Select(error => error.ErrorMessage)));
        }

        var inputValidation = await optionInputValidator.ValidateAsync((value, measurementUnitId), ct);
        if (!inputValidation.IsValid)
        {
            return Result.Failure<long>(ValidationError(inputValidation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(attributeBlueprintId, ct);
        if (blueprint is null) return NotFound<long>();

        var measurementUnitError = await FindMissingMeasurementUnitAsync(blueprint.DataType, [measurementUnitId], ct);
        if (measurementUnitError is not null)
        {
            return Result.Failure<long>(measurementUnitError);
        }

        var optionId = idGenerator.Generate();
        try { blueprint.AddOption(optionId, value, measurementUnitId, clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure<long>(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(optionId);
    }

    public async Task<Result> UpdateOptionAsync(long attributeBlueprintId, long optionId, string value, long? measurementUnitId,
        CancellationToken ct = default)
    {
        var idsValidation = await optionIdsValidator.ValidateAsync((attributeBlueprintId, optionId), ct);
        if (!idsValidation.IsValid)
        {
            return Result.Failure(ValidationError(idsValidation.Errors.Select(error => error.ErrorMessage)));
        }

        var inputValidation = await optionInputValidator.ValidateAsync((value, measurementUnitId), ct);
        if (!inputValidation.IsValid)
        {
            return Result.Failure(ValidationError(inputValidation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(attributeBlueprintId, ct);
        if (blueprint is null) return NotFound();
        if (!blueprint.Options.Any(option => option.Id == optionId)) return OptionNotFound();

        var measurementUnitError = await FindMissingMeasurementUnitAsync(blueprint.DataType, [measurementUnitId], ct);
        if (measurementUnitError is not null)
        {
            return Result.Failure(measurementUnitError);
        }

        try { blueprint.UpdateOption(optionId, value, measurementUnitId, clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> RemoveOptionAsync(long attributeBlueprintId, long optionId, CancellationToken ct = default)
    {
        var validation = await optionIdsValidator.ValidateAsync((attributeBlueprintId, optionId), ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(attributeBlueprintId, ct);
        if (blueprint is null) return NotFound();
        if (!blueprint.Options.Any(option => option.Id == optionId)) return OptionNotFound();

        var productValueCount = await dependencyGateway.CountProductValuesForOptionAsync(attributeBlueprintId, optionId, ct);

        try { blueprint.RemoveOption(optionId, clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        if (productValueCount > 0)
        {
            await dependencyGateway.RemoveProductValuesForOptionAsync(attributeBlueprintId, optionId, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ActivateAsync(long id, CancellationToken ct = default)
    {
        var validation = await idValidator.ValidateAsync(id, ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        if (blueprint.Status == AttributeBlueprintStatus.Archived)
        {
            return Result.Failure(Archived());
        }

        try { blueprint.Activate(clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeactivateAsync(long id, CancellationToken ct = default)
    {
        var validation = await idValidator.ValidateAsync(id, ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        if (blueprint.Status == AttributeBlueprintStatus.Archived)
        {
            return Result.Failure(Archived());
        }

        if (await dependencyGateway.CountActiveCategoryAssignmentsAsync(id, ct) > 0)
        {
            return Result.Failure(InUseByCategories());
        }

        try { blueprint.Deactivate(clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ArchiveAsync(long id, CancellationToken ct = default)
    {
        var validation = await idValidator.ValidateAsync(id, ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        if (await dependencyGateway.CountActiveCategoryAssignmentsAsync(id, ct) > 0)
        {
            return Result.Failure(InUseByCategories());
        }

        try { blueprint.Archive(clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> UnarchiveAsync(long id, CancellationToken ct = default)
    {
        var validation = await idValidator.ValidateAsync(id, ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        try { blueprint.Unarchive(clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        var validation = await idValidator.ValidateAsync(id, ct);
        if (!validation.IsValid)
        {
            return Result.Failure(ValidationError(validation.Errors.Select(error => error.ErrorMessage)));
        }

        var blueprint = await repository.GetByIdAsync(id, ct);
        if (blueprint is null) return NotFound();

        var dependencies = await dependencyGateway.GetDependenciesAsync(id, ct);
        if (dependencies.ProductValueCount > 0)
        {
            return Result.Failure(DeleteBlockedByProductValues());
        }

        if (dependencies.CategoryMappingCount > 0)
        {
            await dependencyGateway.RemoveCategoryMappingsAsync(id, ct);
        }

        try { blueprint.Delete(clock.UtcNow); }
        catch (DomainException exception) { return Result.Failure(new Error(exception.Code, exception.Message)); }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }


    private static Result NotFound() => Result.Failure(BlueprintNotFoundError());

    private static Result<T> NotFound<T>() => Result.Failure<T>(BlueprintNotFoundError());

    private static Error BlueprintNotFoundError() => new(
        "ATTRIBUTE_BLUEPRINT_NOT_FOUND", "Attribute blueprint was not found.");

    private static Error NameTaken(string name) => new(
        "ATTRIBUTE_BLUEPRINT_NAME_TAKEN",
        $"An attribute blueprint named '{name}' already exists.");

    private static Error ValidationError(IEnumerable<string> messages) => new(
        "VALIDATION_FAILED",
        string.Join("; ", messages));

    private static Error DataTypeInUse() => new(
        "ATTRIBUTE_BLUEPRINT_DATA_TYPE_IN_USE",
        "Cannot change attribute data type while it contains live product data");

    private static Error DataTypeHasOptions() => new(
        "ATTRIBUTE_BLUEPRINT_DATA_TYPE_HAS_OPTIONS",
        "Remove the predefined options before changing the attribute data type.");

    private static Error Archived() => new(
        "ATTRIBUTE_BLUEPRINT_ARCHIVED",
        "Unarchive the attribute blueprint before changing its status.");

    private static Error InUseByCategories() => new(
        "ATTRIBUTE_BLUEPRINT_IN_USE_BY_CATEGORIES",
        "Cannot deactivate or archive this attribute while it is still assigned to one or more categories.");

    private static Error DeleteBlockedByProductValues() => new(
        "ATTRIBUTE_BLUEPRINT_IN_USE_BY_PRODUCTS",
        "Cannot delete this attribute as it is in use by products. Please remove the attribute values from all products first.");

    private static Result OptionNotFound() => Result.Failure(
        new Error("ATTRIBUTE_BLUEPRINT_OPTION_NOT_FOUND", "The option was not found in this attribute blueprint."));

    private static Error MeasurementUnitNotFound(long measurementUnitId) => new(
        "MEASUREMENT_UNIT_NOT_FOUND", $"Measurement unit '{measurementUnitId}' was not found.");

    private async Task<Error?> FindMissingMeasurementUnitAsync(AttributeBlueprintDataType dataType,
        IEnumerable<long?> measurementUnitIds, CancellationToken ct)
    {
        if (dataType != AttributeBlueprintDataType.Number) return null;

        var unitIds = measurementUnitIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();

        foreach (var unitId in unitIds)
        {
            if (!await measurementUnitRepository.ExistsAsync(unitId, ct))
            {
                return MeasurementUnitNotFound(unitId);
            }
        }

        return null;
    }

}
