namespace DotaAnalyst.Application.Abstractions;

// Thrown by Infrastructure when our own database cannot be read or written. The database is an extra
// layer (OpenDota stays the source of truth), so use cases log it and carry on without it.
public class StorageUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
