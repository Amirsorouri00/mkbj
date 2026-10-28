using EwaysVNext.Domain.Common.Exceptions;

namespace EwaysVNext.Domain.Common;

public interface IBusinessRule
{
    bool IsBroken();
    string Message { get; }
}

public static class BusinessRule
{
    public static void Check(params IBusinessRule[] businessRules)
    {
        foreach (var rule in businessRules)
        {
            if (rule.IsBroken())
                throw new BusinessRuleException(rule.Message);
        }
    }
}
