using System.Text.Json;
using System.Text.Json.Serialization;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Application.Requests;

[JsonConverter(typeof(UpsertProductModelStageRequestJsonConverter))]
public sealed class UpsertProductModelStageRequest
{
    public Guid? SubStageId { get; init; }
    public int? StageOrder { get; init; }
    public decimal? PiecePrice { get; init; }
    public decimal? StandardSeconds { get; init; }
    // Omission preserves the current value; an explicit null clears it.
    public bool HasStandardSeconds { get; init; }
    public CompensationMode? CompensationMode { get; init; }
    public string? InvalidCompensationMode { get; init; }
    public bool? IsRequired { get; init; }
    public bool? IsActive { get; init; }
    public DateTime? EffectiveFrom { get; init; }
    // Omission preserves the current value; an explicit null clears it.
    public bool HasEffectiveFrom { get; init; }
}

public sealed class UpsertProductModelStageRequestJsonConverter : JsonConverter<UpsertProductModelStageRequest>
{
    public override UpsertProductModelStageRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        T? Read<T>(string name) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.Deserialize<T>(options)
            : default;

        var (compensationMode, invalidCompensationMode) = ReadCompensationMode(root);

        return new UpsertProductModelStageRequest
        {
            SubStageId = Read<Guid?>("subStageId"),
            StageOrder = Read<int?>("stageOrder"),
            PiecePrice = Read<decimal?>("piecePrice"),
            StandardSeconds = Read<decimal?>("standardSeconds"),
            HasStandardSeconds = root.TryGetProperty("standardSeconds", out _),
            CompensationMode = compensationMode,
            InvalidCompensationMode = invalidCompensationMode,
            IsRequired = Read<bool?>("isRequired"),
            IsActive = Read<bool?>("isActive"),
            EffectiveFrom = Read<DateTime?>("effectiveFrom"),
            HasEffectiveFrom = root.TryGetProperty("effectiveFrom", out _)
        };
    }

    private static (CompensationMode? Value, string? Error) ReadCompensationMode(JsonElement root)
    {
        if (!root.TryGetProperty("compensationMode", out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (null, null);
        }

        if (value.ValueKind == JsonValueKind.String &&
            Enum.TryParse<CompensationMode>(value.GetString(), ignoreCase: false, out var mode) &&
            Enum.IsDefined(mode))
        {
            return (mode, null);
        }

        return (null, "compensationMode must be one of SharedPercentage, FullRatePerWorker, or FixedAmount.");
    }

    public override void Write(Utf8JsonWriter writer, UpsertProductModelStageRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.SubStageId.HasValue) writer.WriteString("subStageId", value.SubStageId.Value);
        if (value.StageOrder.HasValue) writer.WriteNumber("stageOrder", value.StageOrder.Value);
        if (value.PiecePrice.HasValue) writer.WriteNumber("piecePrice", value.PiecePrice.Value);
        if (value.HasStandardSeconds || value.StandardSeconds.HasValue)
        {
            writer.WritePropertyName("standardSeconds");
            if (value.StandardSeconds.HasValue) writer.WriteNumberValue(value.StandardSeconds.Value); else writer.WriteNullValue();
        }
        if (value.CompensationMode.HasValue) writer.WriteString("compensationMode", value.CompensationMode.Value.ToString());
        if (value.IsRequired.HasValue) writer.WriteBoolean("isRequired", value.IsRequired.Value);
        if (value.IsActive.HasValue) writer.WriteBoolean("isActive", value.IsActive.Value);
        if (value.HasEffectiveFrom || value.EffectiveFrom.HasValue)
        {
            writer.WritePropertyName("effectiveFrom");
            if (value.EffectiveFrom.HasValue) writer.WriteStringValue(value.EffectiveFrom.Value); else writer.WriteNullValue();
        }
        writer.WriteEndObject();
    }
}
