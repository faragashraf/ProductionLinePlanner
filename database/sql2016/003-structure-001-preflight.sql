/*
 STRUCTURE-001 preflight report — read-only. Run this before applying
 CorrectDepartmentLineStageHierarchy. It never modifies data.
*/
SET NOCOUNT ON;

/* Existing production lines will intentionally remain unassigned after the migration. */
IF COL_LENGTH(N'ProductionLines', N'DepartmentId') IS NULL
    SELECT [l].[Id] AS [ProductionLineId], [l].[FactoryId], [l].[LineCode], [l].[Name], CAST(NULL AS uniqueidentifier) AS [DepartmentId]
    FROM [ProductionLines] AS [l]
    ORDER BY [l].[FactoryId], [l].[Name];
ELSE
    SELECT [l].[Id] AS [ProductionLineId], [l].[FactoryId], [l].[LineCode], [l].[Name], [l].[DepartmentId]
    FROM [ProductionLines] AS [l]
    WHERE [l].[DepartmentId] IS NULL
    ORDER BY [l].[FactoryId], [l].[Name];

/* Every SubStage must already resolve through MainStage to a valid production line. */
IF COL_LENGTH(N'SubStages', N'ProductionLineId') IS NULL
    SELECT [s].[Id] AS [SubStageId], [s].[MainStageId], [m].[ProductionLineId] AS [ExpectedProductionLineId], CAST(NULL AS uniqueidentifier) AS [ActualProductionLineId],
        CASE WHEN [m].[Id] IS NULL THEN N'OrphanMainStage' WHEN [l].[Id] IS NULL THEN N'OrphanProductionLine' ELSE N'ValidForBackfill' END AS [Finding]
    FROM [SubStages] AS [s]
    LEFT JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId]
    LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId]
    WHERE [m].[Id] IS NULL OR [l].[Id] IS NULL
    ORDER BY [s].[Id];
ELSE
    SELECT [s].[Id] AS [SubStageId], [s].[MainStageId], [m].[ProductionLineId] AS [ExpectedProductionLineId], [s].[ProductionLineId] AS [ActualProductionLineId],
        CASE WHEN [m].[Id] IS NULL THEN N'OrphanMainStage' WHEN [l].[Id] IS NULL THEN N'OrphanProductionLine' ELSE N'LineConflict' END AS [Finding]
    FROM [SubStages] AS [s]
    LEFT JOIN [MainStages] AS [m] ON [m].[Id] = [s].[MainStageId]
    LEFT JOIN [ProductionLines] AS [l] ON [l].[Id] = [m].[ProductionLineId]
    WHERE [m].[Id] IS NULL OR [l].[Id] IS NULL OR [s].[ProductionLineId] IS NULL OR [s].[ProductionLineId] <> [m].[ProductionLineId]
    ORDER BY [s].[Id];

/* Codes that are not eligible to set the new STG sequence's starting value. */
SELECT [Id] AS [SubStageId], [Code]
FROM [SubStages]
WHERE NOT (
    LEN([Code]) > 3
    AND UPPER(LEFT([Code], 3)) = N'STG'
    AND SUBSTRING([Code], 4, 8000) NOT LIKE N'%[^0-9]%'
)
ORDER BY [Code];
