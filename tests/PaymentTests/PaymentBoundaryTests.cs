using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Payment.Application.Domain;
using EwaysVNext.Modules.Payment.Application.Ports.Outbound;
using EwaysVNext.Modules.Payment.Application.Service;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EwaysVNext.PaymentTests;

public class PaymentBoundaryTests
{
    [Fact]
    public void Payment_application_depends_only_on_its_core_and_building_blocks()
    {
        var references = typeof(PaymentGatewayService).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(references, name => name.Contains("Adapter", StringComparison.Ordinal)
            || name.Contains("EntityFrameworkCore", StringComparison.Ordinal)
            || name.Contains("Persistence", StringComparison.Ordinal)
            || name.Contains("AspNetCore", StringComparison.Ordinal)
            || name.Contains("QueryApplication", StringComparison.Ordinal)
            || name.StartsWith("EwaysVNext.Modules.", StringComparison.Ordinal));
    }

    [Fact]
    public void Service_depends_only_on_the_gateway_boundary_and_its_logger()
    {
        var parameters = Assert.Single(typeof(PaymentGatewayService).GetConstructors()).GetParameters()
            .Select(p => p.ParameterType);
        Assert.Equal([typeof(IPaymentGateway), typeof(ILogger<PaymentGatewayService>)], parameters);
    }

    [Fact]
    public void Bank_transaction_is_only_an_aggregate_contract_without_a_concrete_entity()
    {
        Assert.True(typeof(IBankTransaction).IsInterface);
        Assert.True(typeof(IAggregateRoot).IsAssignableFrom(typeof(IBankTransaction)));
        Assert.DoesNotContain(typeof(IBankTransaction).Assembly.GetTypes(),
            type => type.IsClass && typeof(IAggregateRoot).IsAssignableFrom(type));
    }

    [Fact]
    public void Payment_ports_are_interfaces_and_services_are_sealed()
    {
        var types = typeof(PaymentGatewayService).Assembly.GetTypes();
        Assert.All(types.Where(t => t.Namespace?.Contains(".Ports.", StringComparison.Ordinal) == true),
            t => Assert.True(t.IsInterface, $"{t.FullName} is under Ports but is not an interface"));
        Assert.All(types.Where(t => t.IsPublic && t.Namespace?.EndsWith(".Service", StringComparison.Ordinal) == true), t => Assert.True(t.IsSealed));
    }

    [Fact]
    public void Bank_transaction_uses_payments_own_money_contract()
    {
        var moneyType = typeof(IBankTransaction).GetProperty(nameof(IBankTransaction.Amount))!.PropertyType;
        Assert.Equal(typeof(EwaysVNext.Modules.Payment.Application.Domain.ValueObjects.Money), moneyType);
        Assert.Equal(typeof(IBankTransaction).Assembly, moneyType.Assembly);
    }

    [Fact]
    public void Payment_tests_do_not_reference_other_modules_or_the_database()
    {
        var references = typeof(PaymentBoundaryTests).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(references, name => name == "EwaysVNext.Database"
            || (name.StartsWith("EwaysVNext.Modules.", StringComparison.Ordinal)
                && !name.StartsWith("EwaysVNext.Modules.Payment.", StringComparison.Ordinal)));
    }
}
