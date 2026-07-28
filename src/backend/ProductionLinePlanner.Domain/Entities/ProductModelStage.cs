using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class ProductModelStage
{
    private ProductModelStage() { }

    public ProductModelStage(
        Guid id,
        Guid productModelId,
        Guid productionLineId,
        Guid subStageId,
        int stageOrder,
        decimal piecePrice,
        decimal? standardSeconds,
        CompensationMode compensationMode,
        bool isRequired = true,
        bool isActive = true,
        DateTime? effectiveFrom = null,
        DateTime? createdAtUtc = null)
    {
        if (productModelId == Guid.Empty)
            throw new ArgumentException("ProductModelId is required.", nameof(productModelId));
        if (productionLineId == Guid.Empty)
            throw new ArgumentException("ProductionLineId is required.", nameof(productionLineId));
        if (subStageId == Guid.Empty)
            throw new ArgumentException("SubStageId is required.", nameof(subStageId));
        if (stageOrder <= 0)
            throw new ArgumentOutOfRangeException(nameof(stageOrder), "StageOrder must be greater than 0.");
        if (piecePrice < 0)
            throw new ArgumentOutOfRangeException(nameof(piecePrice), "PiecePrice must be greater than or equal to 0.");
        if (standardSeconds.HasValue && standardSeconds.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(standardSeconds), "StandardSeconds must be greater than 0.");

        Id = id;
        ProductModelId = productModelId;
        ProductionLineId = productionLineId;
        SubStageId = subStageId;
        StageOrder = stageOrder;
        PiecePrice = piecePrice;
        StandardSeconds = standardSeconds;
        CompensationMode = compensationMode;
        IsRequired = isRequired;
        IsActive = isActive;
        EffectiveFrom = effectiveFrom;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public Guid ProductModelId { get; private set; }
    public ProductModel? ProductModel { get; set; }
    public Guid ProductionLineId { get; private set; }
    public ProductionLine? ProductionLine { get; set; }
    public Guid SubStageId { get; private set; }
    public SubStage? SubStage { get; set; }
    public int StageOrder { get; private set; }
    public decimal PiecePrice { get; private set; }
    public decimal? StandardSeconds { get; private set; }
    public CompensationMode CompensationMode { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? EffectiveFrom { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(
        Guid subStageId,
        int stageOrder,
        decimal piecePrice,
        decimal? standardSeconds,
        CompensationMode compensationMode,
        bool isRequired,
        bool isActive,
        DateTime? effectiveFrom,
        DateTime? atUtc = null)
    {
        if (subStageId == Guid.Empty)
            throw new ArgumentException("SubStageId is required.", nameof(subStageId));
        if (stageOrder <= 0)
            throw new ArgumentOutOfRangeException(nameof(stageOrder), "StageOrder must be greater than 0.");
        if (piecePrice < 0)
            throw new ArgumentOutOfRangeException(nameof(piecePrice), "PiecePrice must be greater than or equal to 0.");
        if (standardSeconds.HasValue && standardSeconds.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(standardSeconds), "StandardSeconds must be greater than 0.");

        SubStageId = subStageId;
        StageOrder = stageOrder;
        PiecePrice = piecePrice;
        StandardSeconds = standardSeconds;
        CompensationMode = compensationMode;
        IsRequired = isRequired;
        IsActive = isActive;
        EffectiveFrom = effectiveFrom;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Activate(DateTime? atUtc = null)
    {
        IsActive = true;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void Deactivate(DateTime? atUtc = null)
    {
        IsActive = false;
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
