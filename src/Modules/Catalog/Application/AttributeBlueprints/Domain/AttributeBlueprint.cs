using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;

public sealed class AttributeBlueprint : AggregateRoot<long>
{
    public string Name { get; private set; } = null!;

    public AttributeBlueprintDataType DataType { get; private set; }

    public AttributeBlueprintStatus Status { get; private set; }

    public AttributeBlueprintStatus? PreviousStatus { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<AttributeBlueprintOption> _options = new();

    public IReadOnlyCollection<AttributeBlueprintOption> Options => _options.AsReadOnly();


    private AttributeBlueprint() { }

    public static AttributeBlueprint Create(long id, string name, AttributeBlueprintDataType dataType, DateTimeOffset now)
    {
        if (id <= 0)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_ID_INVALID", "Attribute blueprint ID must be positive.");
        }

        EnsureSupportedDataType(dataType);

        var attributeBlueprint = new AttributeBlueprint
        {
            Id = id,
            Name = NormalizeName(name),
            DataType = dataType,
            Status = AttributeBlueprintStatus.Active,
            DeletedAt = null,
            CreatedAt = now,
            UpdatedAt = null
        };

        attributeBlueprint.Raise(new AttributeBlueprintCreatedDomainEvent(attributeBlueprint.Id, attributeBlueprint.Name, attributeBlueprint.DataType));
        return attributeBlueprint;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        EnsureNotDeleted();

        var normalizedName = NormalizeName(name);

        if (Name == normalizedName)
        {
            return;
        }

        Name = normalizedName;
        Touch(now);
        Raise(new AttributeBlueprintRenamedDomainEvent(Id, Name));
    }

    public void ChangeDataType(AttributeBlueprintDataType newDataType, DateTimeOffset now)
    {
        EnsureNotDeleted();
        EnsureSupportedDataType(newDataType);

        if (DataType == newDataType)
            return;

        if (_options.Count > 0)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_DATA_TYPE_HAS_OPTIONS", "Remove the predefined options before changing the attribute data type.");
        }

        var previousDataType = DataType;
        DataType = newDataType;
        Touch(now);
        Raise(new AttributeBlueprintDataTypeChangedDomainEvent(Id, previousDataType, newDataType));
    }

    public void UpdateDetails(string name, AttributeBlueprintDataType dataType, bool isActive,
        IReadOnlyCollection<(long Id, string Value, long? MeasurementUnitId)> options, DateTimeOffset now)
    {
        var (normalizedName, normalizedOptions) = NormalizeDetailsUpdate(name, dataType, options);

        var optionIds = normalizedOptions.Select(option => option.Id).ToHashSet();
        var changed = false;
        foreach (var option in _options.Where(option => !optionIds.Contains(option.Id)).ToArray())
        {
            _options.Remove(option);
            Raise(new AttributeBlueprintOptionRemovedDomainEvent(Id, option.Id));
            changed = true;
        }

        if (DataType != dataType)
        {
            var previousDataType = DataType;
            DataType = dataType;
            Raise(new AttributeBlueprintDataTypeChangedDomainEvent(Id, previousDataType, dataType));
            changed = true;
        }

        foreach (var option in normalizedOptions)
        {
            var existing = _options.FirstOrDefault(current => current.Id == option.Id);
            if (existing is null)
            {
                _options.Add(new AttributeBlueprintOption(option.Id, Id, option.Value, option.MeasurementUnitId));
                Raise(new AttributeBlueprintOptionAddedDomainEvent(Id, option.Id, option.Value, option.MeasurementUnitId));
                changed = true;
            }
            else if (existing.Value != option.Value || existing.MeasurementUnitId != option.MeasurementUnitId)
            {
                existing.Update(option.Value, option.MeasurementUnitId);
                Raise(new AttributeBlueprintOptionUpdatedDomainEvent(Id, option.Id, option.Value, option.MeasurementUnitId));
                changed = true;
            }
        }

        if (Name != normalizedName)
        {
            Name = normalizedName;
            Raise(new AttributeBlueprintRenamedDomainEvent(Id, Name));
            changed = true;
        }

        var status = isActive ? AttributeBlueprintStatus.Active : AttributeBlueprintStatus.Inactive;
        if (Status != status)
        {
            Status = status;
            if (isActive) Raise(new AttributeBlueprintActivatedDomainEvent(Id));
            else Raise(new AttributeBlueprintDeactivatedDomainEvent(Id));
            changed = true;
        }

        if (changed) Touch(now);
    }

    internal void ValidateDetailsUpdate(string name, AttributeBlueprintDataType dataType,
        IReadOnlyCollection<(long Id, string Value, long? MeasurementUnitId)> options)
        => _ = NormalizeDetailsUpdate(name, dataType, options);

    private (string Name, List<(long Id, string Value, long? MeasurementUnitId)> Options) NormalizeDetailsUpdate(
        string name, AttributeBlueprintDataType dataType,
        IReadOnlyCollection<(long Id, string Value, long? MeasurementUnitId)> options)
    {
        EnsureNotDeleted();
        EnsureNotArchived();
        EnsureSupportedDataType(dataType);

        var normalizedName = NormalizeName(name);
        if (options is null)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTIONS_REQUIRED", "Attribute blueprint options are required.");
        }

        var normalizedOptions = new List<(long Id, string Value, long? MeasurementUnitId)>(options.Count);
        var optionIds = new HashSet<long>();
        foreach (var option in options)
        {
            EnsureValidOptionId(option.Id);
            if (!optionIds.Add(option.Id))
            {
                throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_ID_TAKEN", "An option with this ID already exists in the attribute blueprint.");
            }

            var value = NormalizeOptionValue(dataType, option.Value, option.MeasurementUnitId);
            if (normalizedOptions.Any(existing => existing.MeasurementUnitId == option.MeasurementUnitId &&
                string.Equals(existing.Value, value, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN", "An option with this value and unit already exists in the attribute blueprint.");
            }

            normalizedOptions.Add((option.Id, value, option.MeasurementUnitId));
        }

        return (normalizedName, normalizedOptions);
    }

    public void AddOption(long optionId, string value, long? measurementUnitId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        EnsureValidOptionId(optionId);

        if (_options.Any(option => option.Id == optionId))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_ID_TAKEN", "An option with this ID already exists in the attribute blueprint.");
        }

        var normalizedValue = NormalizeOptionValue(DataType, value, measurementUnitId);
        EnsureUniqueOptionValue(normalizedValue, measurementUnitId);

        _options.Add(new AttributeBlueprintOption(optionId, Id, normalizedValue, measurementUnitId));
        Touch(now);
        Raise(new AttributeBlueprintOptionAddedDomainEvent(Id, optionId, normalizedValue, measurementUnitId));
    }

    public void UpdateOption(long optionId, string value, long? measurementUnitId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        var option = GetOption(optionId);
        var normalizedValue = NormalizeOptionValue(DataType, value, measurementUnitId);
        EnsureUniqueOptionValue(normalizedValue, measurementUnitId, optionId);

        if (option.Value == normalizedValue && option.MeasurementUnitId == measurementUnitId)
            return;

        option.Update(normalizedValue, measurementUnitId);
        Touch(now);
        Raise(new AttributeBlueprintOptionUpdatedDomainEvent(Id, optionId, normalizedValue, measurementUnitId));
    }

    public void RemoveOption(long optionId, DateTimeOffset now)
    {
        EnsureNotDeleted();
        var option = GetOption(optionId);

        _options.Remove(option);
        Touch(now);
        Raise(new AttributeBlueprintOptionRemovedDomainEvent(Id, optionId));
    }

    public void Activate(DateTimeOffset now)
    {
        EnsureNotDeleted();
        EnsureNotArchived();

        if (Status == AttributeBlueprintStatus.Active)
            return;

        Status = AttributeBlueprintStatus.Active;
        Touch(now);
        Raise(new AttributeBlueprintActivatedDomainEvent(Id));
    }

    public void Deactivate(DateTimeOffset now)
    {
        EnsureNotDeleted();
        EnsureNotArchived();

        if (Status == AttributeBlueprintStatus.Inactive)
            return;

        Status = AttributeBlueprintStatus.Inactive;
        Touch(now);
        Raise(new AttributeBlueprintDeactivatedDomainEvent(Id));
    }

    public void Archive(DateTimeOffset now)
    {
        EnsureNotDeleted();

        if (Status == AttributeBlueprintStatus.Archived)
            return;

        PreviousStatus = Status;
        Status = AttributeBlueprintStatus.Archived;
        Touch(now);
        Raise(new AttributeBlueprintArchivedDomainEvent(Id));
    }

    public void Unarchive(DateTimeOffset now)
    {
        EnsureNotDeleted();

        if (Status != AttributeBlueprintStatus.Archived)
            return;

        Status = PreviousStatus ?? AttributeBlueprintStatus.Inactive;
        PreviousStatus = null;
        Touch(now);
        Raise(new AttributeBlueprintUnarchivedDomainEvent(Id, Status));
    }

    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt.HasValue)
            return;

        DeletedAt = now;
        Status = AttributeBlueprintStatus.Inactive;
        PreviousStatus = null;
        Touch(now);
        Raise(new AttributeBlueprintDeletedDomainEvent(Id));
    }

    private AttributeBlueprintOption GetOption(long optionId)
    {
        EnsureValidOptionId(optionId);
        return _options.FirstOrDefault(option => option.Id == optionId)
            ?? throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_NOT_FOUND", "The option was not found in this attribute blueprint.");
    }

    private static void EnsureValidOptionId(long optionId)
    {
        if (optionId <= 0)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_ID_INVALID", "Attribute blueprint option ID must be positive.");
        }
    }

    private void EnsureUniqueOptionValue(string value, long? measurementUnitId, long? excludingId = null)
    {
        if (_options.Any(option => option.Id != excludingId && option.MeasurementUnitId == measurementUnitId &&
            string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN", "An option with this value and unit already exists in the attribute blueprint.");
        }
    }

    private static string NormalizeOptionValue(AttributeBlueprintDataType dataType, string value, long? measurementUnitId)
    {
        if (dataType == AttributeBlueprintDataType.Boolean)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_BOOLEAN_OPTIONS_NOT_ALLOWED", "Boolean attribute blueprints use Yes/No and cannot have predefined options.");
        }

        if (dataType == AttributeBlueprintDataType.Number)
        {
            if (!measurementUnitId.HasValue || measurementUnitId.Value <= 0)
            {
                throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_UNIT_REQUIRED", "Numeric options require a positive unit ID.");
            }
        }
        else if (measurementUnitId.HasValue)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_UNIT_NOT_ALLOWED", "Only numeric options can have a unit.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_REQUIRED", "Attribute blueprint option value is required.");
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > 1000)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TOO_LONG", "Attribute blueprint option value cannot exceed 1000 characters.");
        }

        switch (dataType)
        {
            case AttributeBlueprintDataType.Text:
                return normalizedValue;
            case AttributeBlueprintDataType.Number:
                return NormalizeNumber(normalizedValue);
            case AttributeBlueprintDataType.Date:
                if (!DateOnly.TryParseExact(normalizedValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_DATE_INVALID", "Option value must be a valid date in yyyy-MM-dd format.");
                }

                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            default:
                throw new DomainException("ATTRIBUTE_BLUEPRINT_DATA_TYPE_INVALID", "Attribute blueprint data type is invalid.");
        }
    }

    private static string NormalizeNumber(string value)
    {
        var decimalPointIndex = value.IndexOf('.');
        if (decimalPointIndex >= 0 && value.Length - decimalPointIndex - 1 > 28)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_NUMBER_PRECISION_INVALID", "Option value cannot have more than 28 decimal places.");
        }

        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_OPTION_NUMBER_INVALID", "Option value must be a valid decimal number.");
        }

        return number.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_NAME_REQUIRED", "Attribute blueprint name is required.");
        }

        var normalizedName = name.Trim();
        if (normalizedName.Length > 200)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_NAME_TOO_LONG", "Attribute blueprint name cannot exceed 200 characters.");
        }

        return normalizedName;
    }

    private static void EnsureSupportedDataType(AttributeBlueprintDataType dataType)
    {
        if (!Enum.IsDefined(dataType))
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_DATA_TYPE_INVALID", "Attribute blueprint data type is invalid.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (DeletedAt.HasValue)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_DELETED", "A deleted attribute blueprint cannot be modified.");
        }
    }

    private void EnsureNotArchived()
    {
        if (Status == AttributeBlueprintStatus.Archived)
        {
            throw new DomainException("ATTRIBUTE_BLUEPRINT_ARCHIVED", "Unarchive the attribute blueprint before changing its status.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }
}

public enum AttributeBlueprintDataType
{
    Text = 1,
    Number = 2,
    Boolean = 3,
    Date = 4
}

public enum AttributeBlueprintStatus
{
    Active = 1,
    Inactive = 2,
    Archived = 3
}


