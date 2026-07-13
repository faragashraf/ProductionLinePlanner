using System.Text.Json;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Tests;

public sealed class UpsertProductModelStageRequestJsonConverterTests
{
    [Theory]
    [InlineData("SharedPercentage", CompensationMode.SharedPercentage)]
    [InlineData("FullRatePerWorker", CompensationMode.FullRatePerWorker)]
    [InlineData("FixedAmount", CompensationMode.FixedAmount)]
    public void Reads_documented_compensation_mode_names(string modeName, CompensationMode expected)
    {
        var request = JsonSerializer.Deserialize<UpsertProductModelStageRequest>($$"""{"compensationMode":"{{modeName}}","piecePrice":0.50,"standardSeconds":22}""");

        Assert.NotNull(request);
        Assert.Equal(expected, request.CompensationMode);
        Assert.Null(request.InvalidCompensationMode);
        Assert.Equal(0.50m, request.PiecePrice);
        Assert.Equal(22m, request.StandardSeconds);
    }

    [Fact]
    public void Captures_unknown_compensation_mode_for_a_controlled_validation_response()
    {
        var request = JsonSerializer.Deserialize<UpsertProductModelStageRequest>("""{"compensationMode":"UnknownMode"}""");

        Assert.NotNull(request);
        Assert.Null(request.CompensationMode);
        Assert.Equal("compensationMode must be one of SharedPercentage, FullRatePerWorker, or FixedAmount.", request.InvalidCompensationMode);
    }
}
