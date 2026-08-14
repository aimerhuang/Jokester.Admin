namespace jokester.admin.Common;

public static class ApiDateTime
{
    public static DateTime FromUtcStorage(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime FromLocalStorage(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)
    };

    public static DateTime ToLocalStorage(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value.ToLocalTime(),
        DateTimeKind.Local => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Local)
    };
}
