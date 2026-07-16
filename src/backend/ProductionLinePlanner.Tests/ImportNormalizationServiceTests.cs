using ProductionLinePlanner.Infrastructure.Importing;

namespace ProductionLinePlanner.Tests;

public sealed class ImportNormalizationServiceTests
{
    [Theory]
    [InlineData(" أجناب ", "اجناب")]
    [InlineData("إستلام", "استلام")]
    [InlineData("عامل A", "عامل a")]
    public void Lookup_normalization_handles_only_safe_variants(string first, string second)
    {
        var service = new ImportNormalizationService();

        Assert.Equal(service.NormalizeLookup(first), service.NormalizeLookup(second));
    }

    [Fact]
    public void Employee_code_normalization_does_not_use_worker_name_identity()
    {
        var service = new ImportNormalizationService();

        Assert.Equal("102", service.NormalizeEmployeeCode(" 102 "));
        Assert.NotEqual(service.NormalizeEmployeeCode("102"), service.NormalizeEmployeeCode("103"));
    }
}
