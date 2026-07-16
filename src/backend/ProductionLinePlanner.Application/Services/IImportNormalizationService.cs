namespace ProductionLinePlanner.Application.Services;

public interface IImportNormalizationService
{
    string NormalizeLookup(string? value);
    string NormalizeEmployeeCode(string? value);
}
