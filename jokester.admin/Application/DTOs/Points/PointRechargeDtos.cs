namespace jokester.admin.Application.DTOs.Points;

public sealed class RechargePackageDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int Points { get; init; }

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int? ValidityDays { get; init; }

    public int BonusPercent { get; init; }

    public string? BadgeCode { get; init; }

    public bool IsFeatured { get; init; }

    public bool IsFirstPurchaseEligible { get; init; }

    public bool PurchaseEnabled { get; init; }

    public string PurchaseMethod { get; init; } = "external";

    public string? AppleProductId { get; init; }

    public string? AppleProductType { get; init; }

    public int Sort { get; init; }

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> Benefits { get; init; } = [];
}

public sealed class CreateRechargeOrderRequest
{
    public string PackageCode { get; init; } = string.Empty;
}

public sealed class RechargeOrderDto
{
    public string OrderNo { get; init; } = string.Empty;

    public string PackageCode { get; init; } = string.Empty;

    public int Points { get; init; }

    public int? ValidityDays { get; init; }

    public decimal PriceAmount { get; init; }

    public long PriceMinorUnits { get; init; }

    public string Currency { get; init; } = "CNY";

    public int Status { get; init; }

    public string? PurchaseUrl { get; init; }

    public DateTime ExpiresAt { get; init; }

    public DateTime CreatedAt { get; init; }
}

public sealed class RedeemPointCodeRequest
{
    public string Code { get; init; } = string.Empty;
}

public sealed class RedeemPointCodeResponse
{
    public int AddedPoints { get; init; }

    public int AvailablePoints { get; init; }

    public DateTime? ExpiresAt { get; init; }

    public DateTime RedeemedAt { get; init; }
}

public sealed class IssuePointRedeemCodesRequest
{
    public string? PackageCode { get; init; }

    public int? Points { get; init; }

    public int Count { get; init; } = 1;

    public string? OrderNo { get; init; }

    public DateTime? ExpiresAt { get; init; }
}

public sealed class IssuedPointRedeemCodesResponse
{
    public string? PackageCode { get; init; }

    public int Points { get; init; }

    public int? ValidityDays { get; init; }

    public IReadOnlyList<string> Codes { get; init; } = [];
}
