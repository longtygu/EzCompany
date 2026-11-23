using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EzCompany.Core.Data;

internal sealed class UtcDateTimeOffsetTicksConverter()
    : ValueConverter<DateTimeOffset, long>(
        v => v.ToUniversalTime().UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

internal sealed class NullableUtcDateTimeOffsetTicksConverter()
    : ValueConverter<DateTimeOffset?, long?>(
        v => v.HasValue ? v.Value.ToUniversalTime().UtcTicks : null,
        ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null);
