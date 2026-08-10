using System.Collections;
using Grafirio.Bridge.Contracts;

namespace Grafirio.DataAnalysis.Api.Data.Access;

/// <summary>
/// Cagri noktalarinin yazdigi anonim parametre nesnesini
/// (<c>new { Schema = schema, Table = table }</c>) tel uzerinde tasinabilir
/// bicime cevirir.
///
/// Bu sinifin var olma sebebi, Faz 1'deki sozun tutulmasi: cagri noktalari
/// dogrudan baglanti ile bridge arasindaki farki gormeyecek. Dapper anonim
/// nesneyi kendi okuyor; bridge yolunda ayni isi burasi yapiyor.
/// </summary>
public static class QueryParameterCodec
{
    public static IReadOnlyList<QueryParameter> Encode(object? parameters) => parameters switch
    {
        null => [],
        IReadOnlyDictionary<string, object?> map => map.Select(kv => Single(kv.Key, kv.Value)).ToList(),
        IDictionary<string, object?> map => map.Select(kv => Single(kv.Key, kv.Value)).ToList(),
        _ => parameters.GetType()
            .GetProperties()
            .Where(p => p.CanRead)
            .Select(p => Single(p.Name, p.GetValue(parameters)))
            .ToList(),
    };

    private static QueryParameter Single(string name, object? value)
    {
        // Liste parametresi: Dapper `IN @Names` yazan sorgularda bunu tek tek
        // parametrelere aciyor. Metin de bir IEnumerable oldugu icin ozellikle
        // disarida birakiliyor.
        if (value is IEnumerable enumerable and not string and not byte[])
        {
            var items = enumerable.Cast<object?>().ToList();

            // Bos liste icin tur cikarilamiyor; metin varsayiliyor. Dapper bos
            // listeyi zaten hicbir satirla eslesmeyen bir ifadeye ceviriyor.
            var kind = items.FirstOrDefault(i => i is not null) is { } sample
                ? SqlValueCodec.KindOf(sample.GetType())
                : SqlValueKind.Text;

            return new QueryParameter(name, kind, null,
                items.Select(SqlValueCodec.Encode).ToList());
        }

        return new QueryParameter(
            name, SqlValueCodec.KindOf(value?.GetType()), SqlValueCodec.Encode(value));
    }
}
