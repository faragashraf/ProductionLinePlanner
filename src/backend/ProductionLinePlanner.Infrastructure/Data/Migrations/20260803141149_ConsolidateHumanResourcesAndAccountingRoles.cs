using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLinePlanner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateHumanResourcesAndAccountingRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Roles were previously entered as editable custom values. Normalize
            // their user assignments to the product-controlled roles before the
            // startup seed reconciles the intentionally restricted grants.
            migrationBuilder.Sql("""
                DECLARE @canonicalRoleId uniqueidentifier;

                SELECT @canonicalRoleId = [Id]
                FROM [AppRoles]
                WHERE [Role] = N'HumanResources';

                IF @canonicalRoleId IS NULL
                BEGIN
                    SELECT TOP (1) @canonicalRoleId = [Id]
                    FROM [AppRoles]
                    WHERE LOWER(COALESCE([Role], N'')) IN (N'hr', N'humanresources')
                       OR LOWER([Name]) IN (N'hr', N'human resources', N'humanresources')
                    ORDER BY CASE WHEN LOWER([Name]) = N'humanresources' THEN 0 ELSE 1 END, [CreatedAtUtc];

                    IF @canonicalRoleId IS NULL
                    BEGIN
                        SET @canonicalRoleId = NEWID();
                        INSERT INTO [AppRoles] ([Id], [Role], [Name], [Description], [IsSystemRole], [IsActive], [CreatedAtUtc], [UpdatedAtUtc])
                        VALUES (@canonicalRoleId, N'HumanResources', N'HumanResources', N'Human resources role.', CAST(1 AS bit), CAST(1 AS bit), SYSUTCDATETIME(), SYSUTCDATETIME());
                    END
                    ELSE
                    BEGIN
                        UPDATE [AppRoles]
                        SET [Role] = N'HumanResources', [Name] = N'HumanResources', [Description] = N'Human resources role.', [IsSystemRole] = CAST(1 AS bit), [IsActive] = CAST(1 AS bit), [UpdatedAtUtc] = SYSUTCDATETIME()
                        WHERE [Id] = @canonicalRoleId;
                    END
                END

                UPDATE [AppRoles]
                SET [Name] = N'HumanResources', [Description] = N'Human resources role.', [IsSystemRole] = CAST(1 AS bit), [IsActive] = CAST(1 AS bit), [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = @canonicalRoleId;

                INSERT INTO [UserRoles] ([AppUserId], [AppRoleId])
                SELECT DISTINCT [source].[AppUserId], @canonicalRoleId
                FROM [UserRoles] AS [source]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [source].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'hr', N'humanresources') OR LOWER([role].[Name]) IN (N'hr', N'human resources', N'humanresources'))
                  AND NOT EXISTS (SELECT 1 FROM [UserRoles] AS [existing] WHERE [existing].[AppUserId] = [source].[AppUserId] AND [existing].[AppRoleId] = @canonicalRoleId);

                DELETE [permissions]
                FROM [RolePermissions] AS [permissions]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [permissions].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'hr', N'humanresources') OR LOWER([role].[Name]) IN (N'hr', N'human resources', N'humanresources'));

                DELETE [assignments]
                FROM [UserRoles] AS [assignments]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [assignments].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'hr', N'humanresources') OR LOWER([role].[Name]) IN (N'hr', N'human resources', N'humanresources'));

                DELETE FROM [AppRoles]
                WHERE [Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([Role], N'')) IN (N'hr', N'humanresources') OR LOWER([Name]) IN (N'hr', N'human resources', N'humanresources'));
                """);

            migrationBuilder.Sql("""
                DECLARE @canonicalRoleId uniqueidentifier;

                SELECT @canonicalRoleId = [Id]
                FROM [AppRoles]
                WHERE [Role] = N'Accounting';

                IF @canonicalRoleId IS NULL
                BEGIN
                    SELECT TOP (1) @canonicalRoleId = [Id]
                    FROM [AppRoles]
                    WHERE LOWER(COALESCE([Role], N'')) IN (N'accounting', N'accountant')
                       OR LOWER([Name]) IN (N'accounting', N'accountant')
                    ORDER BY CASE WHEN LOWER([Name]) = N'accounting' THEN 0 ELSE 1 END, [CreatedAtUtc];

                    IF @canonicalRoleId IS NULL
                    BEGIN
                        SET @canonicalRoleId = NEWID();
                        INSERT INTO [AppRoles] ([Id], [Role], [Name], [Description], [IsSystemRole], [IsActive], [CreatedAtUtc], [UpdatedAtUtc])
                        VALUES (@canonicalRoleId, N'Accounting', N'Accounting', N'Accounting role.', CAST(1 AS bit), CAST(1 AS bit), SYSUTCDATETIME(), SYSUTCDATETIME());
                    END
                    ELSE
                    BEGIN
                        UPDATE [AppRoles]
                        SET [Role] = N'Accounting', [Name] = N'Accounting', [Description] = N'Accounting role.', [IsSystemRole] = CAST(1 AS bit), [IsActive] = CAST(1 AS bit), [UpdatedAtUtc] = SYSUTCDATETIME()
                        WHERE [Id] = @canonicalRoleId;
                    END
                END

                UPDATE [AppRoles]
                SET [Name] = N'Accounting', [Description] = N'Accounting role.', [IsSystemRole] = CAST(1 AS bit), [IsActive] = CAST(1 AS bit), [UpdatedAtUtc] = SYSUTCDATETIME()
                WHERE [Id] = @canonicalRoleId;

                INSERT INTO [UserRoles] ([AppUserId], [AppRoleId])
                SELECT DISTINCT [source].[AppUserId], @canonicalRoleId
                FROM [UserRoles] AS [source]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [source].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'accounting', N'accountant') OR LOWER([role].[Name]) IN (N'accounting', N'accountant'))
                  AND NOT EXISTS (SELECT 1 FROM [UserRoles] AS [existing] WHERE [existing].[AppUserId] = [source].[AppUserId] AND [existing].[AppRoleId] = @canonicalRoleId);

                DELETE [permissions]
                FROM [RolePermissions] AS [permissions]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [permissions].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'accounting', N'accountant') OR LOWER([role].[Name]) IN (N'accounting', N'accountant'));

                DELETE [assignments]
                FROM [UserRoles] AS [assignments]
                INNER JOIN [AppRoles] AS [role] ON [role].[Id] = [assignments].[AppRoleId]
                WHERE [role].[Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([role].[Role], N'')) IN (N'accounting', N'accountant') OR LOWER([role].[Name]) IN (N'accounting', N'accountant'));

                DELETE FROM [AppRoles]
                WHERE [Id] <> @canonicalRoleId
                  AND (LOWER(COALESCE([Role], N'')) IN (N'accounting', N'accountant') OR LOWER([Name]) IN (N'accounting', N'accountant'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Consolidating role assignments is intentionally irreversible.
        }
    }
}
