using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProductionLinePlanner.Application.Common;
using ProductionLinePlanner.Application.Engines;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;
using ProductionLinePlanner.Infrastructure.Data;

namespace ProductionLinePlanner.Infrastructure.BusinessEngines;

public sealed class AuditEngine : IAuditEngine
{
    private static readonly HashSet<string> SafeProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "WorkerId",
        "SubStageId",
        "FromSubStageId",
        "ToSubStageId",
        "ReplacementForWorkerId",
        "AssignedByUserId",
        "ActorUserId",
        "AppUserId",
        "RefreshTokenId",
        "Reason",
        "ActionType",
        "Status",
        "AssignedAtUtc",
        "StartAtUtc",
        "EndAtUtc",
        "CreatedAtUtc",
        "UpdatedAtUtc",
        "ExpiresAtUtc",
        "RevokedAtUtc",
        "IsRead",
        "ReadAtUtc",
        "IsActive"
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private readonly AppDbContext _dbContext;

    public AuditEngine(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Result> RecordAsync(
        Guid actorUserId,
        AuditActionType actionType,
        string entityType,
        string entityId,
        object? before = null,
        object? after = null,
        string? requestMeta = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            return Task.FromResult(Result.Success());
        }

        var metadata = requestMeta;

        _dbContext.AuditLogs.Add(new AuditLog(
            id: Guid.NewGuid(),
            actorUserId: actorUserId,
            actionType: actionType,
            entityType: entityType,
            entityId: entityId,
            entityBeforeJson: SerializeAuditPayload(before),
            entityAfterJson: SerializeAuditPayload(after),
            requestMeta: metadata,
            createdAtUtc: DateTime.UtcNow));

        _ = cancellationToken;
        return Task.FromResult(Result.Success());
    }

    private static string? SerializeAuditPayload(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        if (IsSimpleType(payload))
        {
            return JsonSerializer.Serialize(payload, AuditJsonOptions);
        }

        var safePayload = BuildSafePayload(payload);
        if (safePayload is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(safePayload, AuditJsonOptions);
    }

    private static object? BuildSafePayload(object payload)
    {
        var payloadType = payload.GetType();
        if (IsSimpleType(payload))
        {
            return payload;
        }

        try
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var properties = payloadType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.CanRead);

            foreach (var property in properties)
            {
                if (!SafeProperties.Contains(property.Name))
                {
                    continue;
                }

                var value = property.GetValue(payload);
                if (value is null)
                {
                    continue;
                }

                if (IsSimpleType(value))
                {
                    result[property.Name] = value;
                }
            }

            if (result.Count == 0)
            {
                return null;
            }

            result["type"] = payloadType.Name;
            return result;
        }
        catch
        {
            return new Dictionary<string, object?>
            {
                ["type"] = payloadType.Name
            };
        }
    }

    private static bool IsSimpleType(object value)
    {
        var valueType = value.GetType();
        return valueType == typeof(string)
            || valueType == typeof(char)
            || valueType == typeof(Guid)
            || valueType == typeof(DateTime)
            || valueType == typeof(DateTimeOffset)
            || valueType == typeof(bool)
            || valueType == typeof(byte)
            || valueType == typeof(short)
            || valueType == typeof(int)
            || valueType == typeof(long)
            || valueType == typeof(float)
            || valueType == typeof(double)
            || valueType == typeof(decimal)
            || valueType == typeof(ushort)
            || valueType == typeof(uint)
            || valueType == typeof(ulong)
            || valueType.IsEnum;
    }
}
