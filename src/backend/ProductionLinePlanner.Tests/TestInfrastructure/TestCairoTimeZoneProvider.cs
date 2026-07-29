using Microsoft.Extensions.Configuration;
using ProductionLinePlanner.Application.Services;
using ProductionLinePlanner.Infrastructure.Time;

namespace ProductionLinePlanner.Tests.TestInfrastructure;

internal static class TestCairoTimeZoneProvider
{
    internal static ICairoTimeZoneProvider Instance { get; } = new CairoTimeZoneProvider(new ConfigurationBuilder().Build());
}
