using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

/// <summary>Stores only a source fingerprint and operational metadata; workbook contents stay outside the database.</summary>
public class ImportBatch
{
    private ImportBatch() { }

    public ImportBatch(Guid id, string idempotencyKey, string sourceReference, Guid createdBy, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("IdempotencyKey is required.", nameof(idempotencyKey));
        if (string.IsNullOrWhiteSpace(sourceReference)) throw new ArgumentException("SourceReference is required.", nameof(sourceReference));
        if (createdBy == Guid.Empty) throw new ArgumentException("CreatedBy is required.", nameof(createdBy));
        Id = id;
        IdempotencyKey = idempotencyKey.Trim();
        SourceReference = sourceReference.Trim();
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
        AppliedAtUtc = createdAtUtc;
        Status = ImportBatchStatus.Applied;
    }

    public Guid Id { get; init; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string SourceReference { get; private set; } = string.Empty;
    public ImportBatchStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? AppliedAtUtc { get; private set; }
}
