using System.Text;
using ProductionLinePlanner.Application.Services;

namespace ProductionLinePlanner.Infrastructure.Importing;

/// <summary>
/// The sole lookup normalizer for real-data intake. It preserves display strings
/// and treats only conservative spelling variants as equivalent for lookup.
/// </summary>
public sealed class ImportNormalizationService : IImportNormalizationService
{
    public string NormalizeLookup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var builder = new StringBuilder(value.Trim().Length);
        foreach (var character in value.Trim())
        {
            builder.Append(character switch
            {
                '\u0622' or '\u0623' or '\u0625' => '\u0627',
                '\u0649' => '\u064A',
                '\u0629' => '\u0647',
                '\u0640' => '\0',
                _ => character
            });
        }

        return string.Concat(builder.ToString().Normalize(NormalizationForm.FormKC)
            .Where(character => character != '\0' && !char.IsWhiteSpace(character)))
            .ToUpperInvariant();
    }

    public string NormalizeEmployeeCode(string? value) => NormalizeLookup(value);
}
