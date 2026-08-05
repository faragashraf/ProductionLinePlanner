using Microsoft.EntityFrameworkCore;
using ProductionLinePlanner.Application.Requests;
using ProductionLinePlanner.Domain.Authorization;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Tests;

public sealed class RoleModelTests
{
    [Fact]
    public void Custom_role_is_data_driven_and_keeps_its_trimmed_name_and_description()
    {
        var role = new AppRole(Guid.NewGuid(), "  Shift Lead  ", "  Leads a shift  ");

        Assert.Null(role.Role);
        Assert.False(role.IsSystemRole);
        Assert.Equal("Shift Lead", role.Name);
        Assert.Equal("Leads a shift", role.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Role_name_cannot_be_empty(string name)
    {
        Assert.Throws<ArgumentException>(() => new AppRole(Guid.NewGuid(), name));
    }

    [Fact]
    public void Role_name_has_a_bounded_length()
    {
        Assert.Throws<ArgumentException>(() => new AppRole(Guid.NewGuid(), new string('a', AppRole.MaxNameLength + 1)));
    }

    [Fact]
    public void System_role_names_are_reserved_and_existing_catalog_roles_remain_system_roles()
    {
        var systemRole = new AppRole(Guid.NewGuid(), UserRole.Admin, "Admin", isSystemRole: true);

        Assert.True(SystemRoleCatalog.IsSystemRoleName("admin"));
        Assert.True(systemRole.IsSystemRole);
        Assert.Equal(UserRole.Admin, systemRole.Role);
    }

    [Fact]
    public void Custom_role_can_be_renamed_and_its_description_can_be_set_or_cleared()
    {
        var role = new AppRole(Guid.NewGuid(), "Shift Lead", "Initial");

        role.UpdateDetails("Night Shift Lead", true, "Updated", true);
        Assert.Equal("Night Shift Lead", role.Name);
        Assert.Equal("Updated", role.Description);

        role.UpdateDetails(null, true, null, null);
        Assert.Null(role.Description);

        role.UpdateDetails(null, true, "   ", null);
        Assert.Null(role.Description);
    }

    [Fact]
    public void System_role_definitions_are_product_controlled_while_custom_roles_remain_mutable()
    {
        var systemRole = new AppRole(Guid.NewGuid(), UserRole.Admin, "Admin", isSystemRole: true);
        var customRole = new AppRole(Guid.NewGuid(), "Shift Lead", "Initial");

        Assert.False(systemRole.CanModifyDefinition);
        Assert.Throws<InvalidOperationException>(() => systemRole.UpdateDetails(null, true, "Changed", null));

        Assert.True(customRole.CanModifyDefinition);
        customRole.UpdateDetails(null, true, "Changed", null);
        Assert.Equal("Changed", customRole.Description);
    }

    [Fact]
    public void Role_description_has_a_shared_five_hundred_character_limit()
    {
        var maximumDescription = new string('x', AppRole.MaxDescriptionLength);
        var role = new AppRole(Guid.NewGuid(), "Shift Lead", maximumDescription);

        Assert.Equal(maximumDescription, role.Description);
        Assert.True(AppRole.IsDescriptionWithinLimit(maximumDescription));
        Assert.False(AppRole.IsDescriptionWithinLimit(new string('x', AppRole.MaxDescriptionLength + 1)));
        Assert.Throws<ArgumentException>(() => new AppRole(Guid.NewGuid(), "Too long", new string('x', AppRole.MaxDescriptionLength + 1)));
    }

    [Fact]
    public void Role_description_update_accepts_the_limit_and_rejects_an_oversized_value()
    {
        var role = new AppRole(Guid.NewGuid(), "Shift Lead", "Initial");
        var maximumDescription = new string('x', AppRole.MaxDescriptionLength);

        role.UpdateDetails(null, true, maximumDescription, null);
        Assert.Equal(maximumDescription, role.Description);

        Assert.Throws<ArgumentException>(() => role.UpdateDetails(null, true, new string('x', AppRole.MaxDescriptionLength + 1), null));
    }

    [Fact]
    public void Omitted_description_preserves_the_existing_value_while_explicit_values_update_it()
    {
        var role = new AppRole(Guid.NewGuid(), "Shift Lead", "Keep me");

        role.UpdateDetails("Renamed", false, null, null);
        Assert.Equal("Keep me", role.Description);

        role.UpdateDetails(null, true, null, null);
        Assert.Null(role.Description);

        role.UpdateDetails(null, true, "  Updated description  ", null);
        Assert.Equal("Updated description", role.Description);
    }

    [Fact]
    public void Role_update_request_distinguishes_omitted_description_from_explicit_null()
    {
        var omitted = System.Text.Json.JsonSerializer.Deserialize<RoleUpdateRequest>("{}");
        var explicitNull = System.Text.Json.JsonSerializer.Deserialize<RoleUpdateRequest>("{\"description\":null}");

        Assert.False(omitted!.HasDescription);
        Assert.True(explicitNull!.HasDescription);
        Assert.Null(explicitNull.Description);
    }

    [Fact]
    public async Task Role_name_has_a_unique_database_constraint_and_existing_assignments_are_id_based()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new AppDbContext(options);
        var role = new AppRole(Guid.NewGuid(), "Shift Lead");
        var user = new AppUser(Guid.NewGuid(), "User", "user@example.com", "hash");
        user.AssignRole(role);
        db.AddRange(role, user);
        await db.SaveChangesAsync();

        var roleType = db.Model.FindEntityType(typeof(AppRole))!;
        Assert.Contains(roleType.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(AppRole.Name)]));
        Assert.Equal(role.Id, (await db.AppUsers.Include(x => x.Roles).SingleAsync()).Roles.Single().Id);
    }

    [Fact]
    public void Retired_database_role_values_are_loaded_as_custom_roles()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new AppDbContext(options);
        var roleProperty = db.Model.FindEntityType(typeof(AppRole))!.FindProperty(nameof(AppRole.Role))!;
        var converter = roleProperty.GetValueConverter()!;

        Assert.Null(converter.ConvertFromProvider("Hr"));
        Assert.Equal(UserRole.Admin, converter.ConvertFromProvider("Admin"));
    }
}
