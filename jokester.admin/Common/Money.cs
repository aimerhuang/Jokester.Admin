namespace jokester.admin.Common;

public static class Money
{
    public static long ToMinorUnits(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
