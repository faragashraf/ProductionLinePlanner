using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProductionLinePlanner.Api.Authorization;
using ProductionLinePlanner.Api.Endpoints;
using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Tests;

public sealed class OperationalReadinessPermissionsTests
{
    [Fact]
    public void Every_readiness_endpoint_requires_structure_stages_assignments_and_attendance_permissions()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization(options => options.AddPermissionPolicies());
        var app = builder.Build();
        app.MapOperationalReadinessEndpoints();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/operational-readiness", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(3, endpoints.Length);
        var expected = new[]
        {
            PermissionAuthorizationExtensions.PolicyName(FactoryStructurePermissions.View),
            PermissionAuthorizationExtensions.PolicyName("stages.view"),
            PermissionAuthorizationExtensions.PolicyName("assignments.view"),
            PermissionAuthorizationExtensions.PolicyName("attendance.view")
        };
        Assert.All(endpoints, endpoint =>
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(item => item.Policy).Where(item => item is not null).ToArray();
            Assert.All(expected, policy => Assert.Contains(policy, policies));
        });
    }
}
