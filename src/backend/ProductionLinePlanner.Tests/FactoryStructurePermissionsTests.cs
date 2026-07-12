using ProductionLinePlanner.Domain.Authorization;

namespace ProductionLinePlanner.Tests;

public sealed class FactoryStructurePermissionsTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("get")]
    public void Reads_require_factory_structure_view(string method)
    {
        Assert.Equal(FactoryStructurePermissions.View, FactoryStructurePermissions.ForHttpMethod(method));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Mutations_require_factory_structure_manage(string method)
    {
        Assert.Equal(FactoryStructurePermissions.Manage, FactoryStructurePermissions.ForHttpMethod(method));
    }
}
