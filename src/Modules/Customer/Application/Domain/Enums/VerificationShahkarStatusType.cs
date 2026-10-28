using System;
using System.Collections.Generic;
using System.Text;

namespace EwaysVNext.Modules.Customer.Application.Domain.Enums;

public enum VerificationShahkarStatusType
{
    /// <summary>
    /// Registration is pending approval by admin or agent
    /// </summary>
    PendingApproval = 1,

    /// <summary>
    /// Registration has been approved
    /// </summary>
    Approved = 2,

    /// <summary>
    /// Registration has been rejected
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// Registration is complete
    /// </summary>
    Completed = 4
}
