using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Clawsharp.Memory.Sqlite;

/// <summary>
/// Enforces ISO 8601 round-trip format ("O") for <see cref="DateTimeOffset"/> values stored as TEXT in SQLite.
/// Rejects non-conforming formats on read via <see cref="DateTimeOffset.ParseExact(string, string, IFormatProvider)"/>.
/// </summary>
internal sealed class Iso8601DateTimeOffsetConverter : ValueConverter<DateTimeOffset, string>
{
    public Iso8601DateTimeOffsetConverter()
        : base(
            v => v.ToString("O"),
            v => DateTimeOffset.ParseExact(v, "O", CultureInfo.InvariantCulture))
    {
    }
}

/// <summary>
/// Nullable variant of <see cref="Iso8601DateTimeOffsetConverter"/> for optional <see cref="DateTimeOffset"/> properties.
/// </summary>
internal sealed class NullableIso8601DateTimeOffsetConverter : ValueConverter<DateTimeOffset?, string?>
{
    public NullableIso8601DateTimeOffsetConverter()
        : base(
            v => v.HasValue ? v.Value.ToString("O") : null,
            v => v != null ? DateTimeOffset.ParseExact(v, "O", CultureInfo.InvariantCulture) : null)
    {
    }
}
