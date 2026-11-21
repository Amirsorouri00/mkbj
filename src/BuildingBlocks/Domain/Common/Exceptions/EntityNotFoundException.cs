using System.Net;

namespace Mkbj.Domain.Common.Exceptions;

public sealed class EntityNotFoundException(string message) : BaseException(message, HttpStatusCode.NotFound);
