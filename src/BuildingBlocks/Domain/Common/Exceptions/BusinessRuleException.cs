using System.Net;

namespace Mkbj.Domain.Common.Exceptions;

public sealed class BusinessRuleException(string message) : BaseException(message, HttpStatusCode.BadRequest);
