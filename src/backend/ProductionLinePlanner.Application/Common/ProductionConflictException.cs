namespace ProductionLinePlanner.Application.Common;

public sealed class ProductionConflictException(string message) : InvalidOperationException(message);
