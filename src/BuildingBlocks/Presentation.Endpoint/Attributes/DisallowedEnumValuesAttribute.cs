using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Presentation.Endpoint.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public sealed class DisallowedEnumValuesAttribute<TEnum>(params TEnum[] disallowedValues) : Attribute, IEndpointFilter
    where TEnum : struct, Enum
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.FirstOrDefault(a => a is TEnum);
        if (argument is TEnum value && disallowedValues.Contains(value))
            return ValueTask.FromResult<object?>(Results.BadRequest(DisallowedEnumValuesAttributeMessages.InvalidValue));

        return next(context);
    }
}
