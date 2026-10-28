using System.Net;

namespace EwaysVNext.Domain.Common.Exceptions;

public sealed class EntityNotFoundException(string message) : BaseException(message, HttpStatusCode.NotFound);
