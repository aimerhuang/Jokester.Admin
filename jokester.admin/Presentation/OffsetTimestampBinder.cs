using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace jokester.admin.Presentation;

/// <summary>Require explicit offsets so admin ledger ranges never depend on the API host's timezone.</summary>
public sealed class OffsetTimestampBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var raw = context.ValueProvider.GetValue(context.ModelName);
        if (raw == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, raw);
        var text = raw.FirstValue;
        var hasOffset = text is not null && (text.EndsWith('Z') ||
            (text.Length >= 6 && text[^6] is '+' or '-' && text[^3] == ':'));
        if (hasOffset && DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mmK"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            context.Result = ModelBindingResult.Success(timestamp);
        else
            context.ModelState.TryAddModelError(context.ModelName, "请使用包含时区的 ISO 8601 时间");
        return Task.CompletedTask;
    }
}
