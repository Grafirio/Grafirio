using System.Collections.Concurrent;
using System.Reflection;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Satiri cagri noktasinin istedigi tipe esler — Dapper'in dogrudan baglanti
/// yolunda yaptigi isin bridge yolundaki karsiligi.
///
/// Iki bicim destekleniyor, ikisi de mevcut cagri noktalarinda kullaniliyor:
///
///   * Skaler (<c>QueryAsync&lt;string&gt;</c>): ilk kolonun degeri. Kolon
///     adlarini ve birincil anahtarlari okuyan sorgular boyle yaziyor.
///   * Nesne (<c>QueryAsync&lt;ColumnRow&gt;</c>): kolon adi → ozellik adi.
/// </summary>
public static class QueryRowMapper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    public static T Map<T>(QueryRow row)
    {
        var target = typeof(T);
        var underlying = Nullable.GetUnderlyingType(target) ?? target;

        if (IsScalar(underlying))
        {
            var value = row.Values.Values.FirstOrDefault();
            if (value is null) return default!;
            return (T)ConvertTo(value, underlying);
        }

        var instance = Activator.CreateInstance<T>();

        foreach (var property in PropertyCache.GetOrAdd(target,
                     t => t.GetProperties().Where(p => p.CanWrite).ToArray()))
        {
            if (!row.Values.TryGetValue(property.Name, out var value) || value is null) continue;

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType)
                               ?? property.PropertyType;

            property.SetValue(instance, ConvertTo(value, propertyType));
        }

        return instance;
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive || type.IsEnum
        || type == typeof(string) || type == typeof(decimal)
        || type == typeof(DateTime) || type == typeof(DateTimeOffset)
        || type == typeof(Guid) || type == typeof(byte[]);

    /// <summary>
    /// <see cref="SqlValueCodec"/> tam sayilari <c>long</c>, ondaliklari
    /// <c>decimal</c> olarak cozuyor; cagri noktalari ise <c>int</c>, <c>bool</c>
    /// gibi daha dar tipler bekliyor. Daraltma burada, tek yerde.
    /// </summary>
    private static object ConvertTo(object value, Type target)
    {
        if (target.IsInstanceOfType(value)) return value;

        if (target.IsEnum) return Enum.ToObject(target, Convert.ToInt64(value));

        // SQL Server BIT'i 0/1 doner; hedef bool ise Convert bunu zaten cozer.
        return Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
    }
}
