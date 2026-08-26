using jokester.admin.Common;
using jokester.admin.Common.Exceptions;
using jokester.admin.Domain.Entities;

namespace jokester.admin.Application.Services;

internal static class PointRechargePackageCatalog
{
    internal const string MonthlyCode = "monthly";

    internal static readonly string[] SelectableCodes =
    [
        MonthlyCode,
        "trial",
        "basic",
        "value"
    ];

    internal static bool IsSelectable(string packageCode) =>
        SelectableCodes.Contains(packageCode, StringComparer.Ordinal);

    internal static void EnsureComplete(IReadOnlyCollection<PointRechargePackageEntity> packages)
    {
        if (packages.Count != SelectableCodes.Length
            || SelectableCodes.Any(code => packages.All(package => package.PackageCode != code)))
        {
            throw InvalidConfiguration("The four selectable recharge packages are not fully configured.");
        }

        foreach (var package in packages)
        {
            EnsureMonthlyContract(package);
        }
    }

    internal static void EnsureSelectable(PointRechargePackageEntity package)
    {
        if (!IsSelectable(package.PackageCode))
        {
            throw InvalidConfiguration($"Recharge package {package.PackageCode} is not selectable.");
        }

        EnsureMonthlyContract(package);
    }

    internal static void EnsureApplePoints(PointRechargePackageEntity package, int applePoints)
    {
        EnsureSelectable(package);
        if (package.PackageCode == MonthlyCode && applePoints != 5_000)
        {
            throw InvalidConfiguration("The Apple monthly package must grant exactly 5000 points.");
        }
    }

    internal static void EnsureAwardSnapshot(
        PointRechargePackageEntity package,
        int points,
        int? validityDays)
    {
        EnsureSelectable(package);
        var normalizedValidityDays = validityDays is > 0 ? validityDays : null;
        if (package.PackageCode == MonthlyCode
            && (points != 5_000 || normalizedValidityDays != 30))
        {
            throw InvalidConfiguration("The monthly point award snapshot must be 5000 points for 30 days.");
        }

        if (package.PackageCode != MonthlyCode && normalizedValidityDays.HasValue)
        {
            throw InvalidConfiguration("The trial, basic, and value point award snapshots must be permanent.");
        }
    }

    private static void EnsureMonthlyContract(PointRechargePackageEntity package)
    {
        if (package.PackageCode == MonthlyCode
            && (package.Points != 5_000
                || package.ValidityDays != 30
                || package.RepeatPoints.HasValue))
        {
            throw InvalidConfiguration("The monthly package must always grant 5000 points for 30 days.");
        }

        if (package.PackageCode != MonthlyCode && package.ValidityDays is > 0)
        {
            throw InvalidConfiguration("The trial, basic, and value packages must grant permanent points.");
        }
    }

    private static AppException InvalidConfiguration(string message) =>
        new(ErrorCodes.ServerError, message);
}
