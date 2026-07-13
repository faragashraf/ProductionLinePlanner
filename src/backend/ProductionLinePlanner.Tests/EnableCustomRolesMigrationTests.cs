using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ProductionLinePlanner.Infrastructure.Data.Migrations;

namespace ProductionLinePlanner.Tests;

public sealed class EnableCustomRolesMigrationTests
{
    [Fact]
    public void Up_guards_duplicate_role_names_before_any_schema_change()
    {
        var migration = new EnableCustomRoles();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(Migration).GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!;

        up.Invoke(migration, [builder]);

        var guard = Assert.IsType<SqlOperation>(builder.Operations.First());
        Assert.Contains("GROUP BY [Name]", guard.Sql);
        Assert.Contains("HAVING COUNT(*) > 1", guard.Sql);
        Assert.Contains("THROW 51001", guard.Sql);
        Assert.Contains("duplicate AppRoles.Name values", guard.Sql);
        Assert.DoesNotContain("SET [Name]", guard.Sql);
    }

    [Fact]
    public void Down_guards_against_custom_roles_before_changing_schema()
    {
        var migration = new EnableCustomRoles();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        var down = typeof(Migration).GetMethod("Down", BindingFlags.Instance | BindingFlags.NonPublic)!;

        down.Invoke(migration, [builder]);

        var guard = Assert.IsType<SqlOperation>(builder.Operations.First());
        Assert.Contains("WHERE [Role] IS NULL", guard.Sql);
        Assert.Contains("THROW 51000", guard.Sql);
        Assert.Contains("custom roles exist", guard.Sql);
    }
}
