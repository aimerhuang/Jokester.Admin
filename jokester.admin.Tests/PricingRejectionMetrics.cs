using System.Diagnostics.Metrics;
using jokester.admin.Common;
using jokester.admin.Common.Exceptions;

namespace jokester.admin.Tests;

internal static class PricingRejectionMetrics
{
    internal static async Task<AppException> AssertMissingPriceAsync(string modelCode, Func<Task> operation)
    {
        using var listener = new MeterListener();
        var observing = new AsyncLocal<bool>();
        long total = 0;
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "Jokester.AiImage.Pricing" && instrument.Name == "ai.image.pricing.rejections")
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (!observing.Value) return;
            string? model = null;
            string? reason = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "model") model = tag.Value as string;
                if (tag.Key == "reason") reason = tag.Value as string;
            }
            if (model == modelCode && reason == MachineErrorCodes.ImagePriceNotConfigured)
                Interlocked.Add(ref total, value);
        });
        listener.Start();
        observing.Value = true;
        var exception = await Assert.ThrowsAsync<AppException>(operation);
        Assert.Equal(1L, Interlocked.Read(ref total));
        return exception;
    }
}
