using System.Net;

namespace EwaysVNext.Domain.Common.Exceptions;

public sealed class BusinessRuleException(string message) : BaseException(message, HttpStatusCode.BadRequest);
