namespace DotaAnalyst.Application.Abstractions;

// Thrown by Infrastructure when an external data source cannot be reached or returns an invalid response.
public class DataSourceUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
