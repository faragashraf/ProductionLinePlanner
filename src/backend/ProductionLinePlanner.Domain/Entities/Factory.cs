using System;
using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class Factory
{
    private Factory() { }

    public Factory(
        Guid id,
        string name,
        string code,
        string? location = null,
        bool isActive = true,
        DateTime? createdAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Factory name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Factory code is required.", nameof(code));

        Id = id;
        Name = name.Trim();
        Code = code.Trim();
        Location = location?.Trim();
        IsActive = isActive;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public string? Location { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<ProductionLine> ProductionLines { get; } = [];

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

    public void SetCode(string code, DateTime? atUtc = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Factory code is required.", nameof(code));
        Code = code.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }

    public void SetLocation(string? location, DateTime? atUtc = null)
    {
        Location = location?.Trim();
        UpdatedAtUtc = atUtc ?? DateTime.UtcNow;
    }
}
