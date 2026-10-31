namespace EwaysVNext.Modules.Customer.Application.Customers.Domain;

/// <summary>
/// How this customer came to exist, which decides who owns their details.
/// <para>
/// A Website customer owns their own profile and their email is verified by Keycloak. A
/// PartnerLinked customer was created by a partner placing an order on their behalf: the partner
/// is the source of their contact details, and nobody has proven the address belongs to them.
/// The two are kept apart because almost every rule differs between them.
/// </para>
/// </summary>
public enum CustomerKind
{
    Website = 0,
    PartnerLinked = 1,
}

public enum CustomerStatus
{
    Active = 0,
    Suspended = 1,
}

/// <summary>The fields whose changes are recorded. Contact details only — these are what a dispute turns on.</summary>
public enum ContactField
{
    Email = 0,
    Phone = 1,
    FullName = 2,
}
