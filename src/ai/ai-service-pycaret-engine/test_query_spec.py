"""
Sorgu uretiminin degismezleri.

Calistirma (bagimlilik gerekmez, pytest de gerekmez):

    python test_query_spec.py

Neden pytest yok: bu servisin requirements.txt'inde test bagimliligi yok ve
uretim imajina test paketi eklemek istemiyoruz. Dosya kendi kendine yeter.

Burada korunan sey, sorgu uretiminin gorunmeyen kurallari:

  * Toplulastirmada 1:N join reddedilir — kabul edilseydi COUNT(*) sessizce
    baska bir seyi sayardi.
  * TOP kullanan her sorgunun siralamasi benzersizdir — olmasaydi ayni soru
    iki farkli cevap dondurebilirdi.
  * Filtre degerleri SQL metnine hicbir kosulda girmez.
  * Butun kolon referanslari takma adla nitelenir, tanimlayicilar kacisir.

Bunlarin hicbiri gozle bakarak fark edilmez; kirildiginda da sorgu calismaya
devam eder, yalnizca cevap yanlis olur. Test etmenin sebebi bu.
"""

import sys
import types

FAILS = []


def check(name, got, want):
    if got != want:
        FAILS.append(f"{name}\n  beklenen: {want}\n  gelen   : {got}")


def expect_error(name, fn, fragment, error_type):
    try:
        fn()
    except error_type as e:
        if fragment not in str(e):
            FAILS.append(f"{name}: hata mesajı beklenmedik -> {e}")
        return
    FAILS.append(f"{name}: hata bekleniyordu, gelmedi")


# ── Bolum 1: SQL ureteci (yalnizca stdlib) ────────────────────────────────

from query_spec import (  # noqa: E402
    Aggregate, AliasFactory, ColumnRef, Join, JoinCondition, OrderBy,
    Predicate, QuerySpec, QuerySpecError, TableRef, render, render_group_count,
    MANY_TO_ONE, ONE_TO_MANY,
)

aliases = AliasFactory()
base = TableRef("dbo", "Shipments", aliases.take())
country = ColumnRef(base.alias, "ReceiverCompanyCountryName")

grouped_count = QuerySpec(
    base=base,
    group_by=[country],
    aggregate=Aggregate("count", None, "value"),
    order_by=[OrderBy("value", "desc"), OrderBy(country, "asc")],
    limit=5,
)

check(
    "sayma + benzersiz sıralama",
    render(grouped_count),
    "SELECT TOP (5) [t0].[ReceiverCompanyCountryName], COUNT(*) AS [value] "
    "FROM [dbo].[Shipments] AS [t0] "
    "GROUP BY [t0].[ReceiverCompanyCountryName] "
    "ORDER BY [value] DESC, [t0].[ReceiverCompanyCountryName] ASC",
)

check(
    "toplam grup sayısı aynı filtreleri kullanır",
    render_group_count(grouped_count),
    "SELECT COUNT(*) FROM (SELECT [t0].[ReceiverCompanyCountryName] "
    "FROM [dbo].[Shipments] AS [t0] "
    "GROUP BY [t0].[ReceiverCompanyCountryName]) AS g",
)

amount = ColumnRef(base.alias, "Tutar")
shipped = ColumnRef(base.alias, "SevkTarihi")
filtered_sum = QuerySpec(
    base=base,
    group_by=[country],
    aggregate=Aggregate("sum", amount, "value"),
    where=[Predicate(shipped, ">=", ["p0"]), Predicate(shipped, "<", ["p1"])],
    order_by=[OrderBy("value", "desc"), OrderBy(country, "asc")],
    limit=3,
)
sql = render(filtered_sum)
check(
    "tarih aralıklı toplam",
    sql,
    "SELECT TOP (3) [t0].[ReceiverCompanyCountryName], SUM([t0].[Tutar]) AS [value] "
    "FROM [dbo].[Shipments] AS [t0] "
    "WHERE [t0].[SevkTarihi] >= :p0 AND [t0].[SevkTarihi] < :p1 "
    "GROUP BY [t0].[ReceiverCompanyCountryName] "
    "ORDER BY [value] DESC, [t0].[ReceiverCompanyCountryName] ASC",
)
if "'" in sql:
    FAILS.append("filtre değeri SQL metnine sızmış — parametre olmalıydı")

check(
    "IN listesi parametreye bağlanır",
    render(QuerySpec(
        base=base, group_by=[country], aggregate=Aggregate("count", None, "value"),
        where=[Predicate(country, "IN", ["p0", "p1"])],
        order_by=[OrderBy("value", "desc"), OrderBy(country, "asc")], limit=5,
    )).split("WHERE ")[1].split(" GROUP")[0],
    "[t0].[ReceiverCompanyCountryName] IN (:p0, :p1)",
)

check(
    "satır bazlı örnek okuma",
    render(QuerySpec(base=base, limit=50000, select_all=True, unordered_sample=True)),
    "SELECT TOP (50000) [t0].* FROM [dbo].[Shipments] AS [t0]",
)

# N:1 join — Faz 3'un uretecegi bicim. Simdiden sabitleniyor.
companies = TableRef("dbo", "Companies", aliases.take())
label = ColumnRef(companies.alias, "CountryName")
check(
    "N:1 join LEFT JOIN üretir",
    render(QuerySpec(
        base=base,
        joins=[Join(
            companies,
            [JoinCondition(ColumnRef(companies.alias, "Id"),
                           ColumnRef(base.alias, "ReceiverCompanyId"))],
            MANY_TO_ONE, "left")],
        group_by=[label], aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value", "desc"), OrderBy(label, "asc")], limit=5,
    )),
    "SELECT TOP (5) [t1].[CountryName], COUNT(*) AS [value] "
    "FROM [dbo].[Shipments] AS [t0] "
    "LEFT JOIN [dbo].[Companies] AS [t1] ON [t1].[Id] = [t0].[ReceiverCompanyId] "
    "GROUP BY [t1].[CountryName] "
    "ORDER BY [value] DESC, [t1].[CountryName] ASC",
)

lines = TableRef("dbo", "ShipmentLines", "t9")
expect_error(
    "1:N join toplulaştırmada reddedilir",
    lambda: render(QuerySpec(
        base=base,
        joins=[Join(lines, [JoinCondition(ColumnRef("t9", "ShipmentId"),
                                          ColumnRef(base.alias, "Id"))], ONE_TO_MANY)],
        group_by=[country], aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value"), OrderBy(country, "asc")], limit=5)),
    "bire-çok", QuerySpecError,
)

expect_error(
    "sıralamasız TOP reddedilir",
    lambda: render(QuerySpec(
        base=base, group_by=[country],
        aggregate=Aggregate("count", None, "value"), limit=5)),
    "Sıralaması olmayan", QuerySpecError,
)

expect_error(
    "eşitlik bozucusu olmayan sıralama reddedilir",
    lambda: render(QuerySpec(
        base=base, group_by=[country], aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value", "desc")], limit=5)),
    "benzersiz olmalı", QuerySpecError,
)

expect_error(
    "tanımsız takma ad reddedilir",
    lambda: render(QuerySpec(
        base=base, group_by=[ColumnRef("tX", "Foo")],
        aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value"), OrderBy(ColumnRef("tX", "Foo"))], limit=5)),
    "tanımlı değil", QuerySpecError,
)

expect_error(
    "kolonsuz SUM reddedilir",
    lambda: render(QuerySpec(
        base=base, group_by=[country], aggregate=Aggregate("sum", None, "value"),
        order_by=[OrderBy("value"), OrderBy(country, "asc")], limit=5)),
    "için bir kolon gerekiyor", QuerySpecError,
)

if "[Bad]]Name]" not in render(QuerySpec(
        base=TableRef("dbo", "Bad]Name", "t0"), select_all=True)):
    FAILS.append("köşeli parantez kaçışı yapılmamış")


# ── Bolum 2: AgentAnalyzer'in DB gerektirmeyen mantigi ────────────────────
#
# pandas ve sqlalchemy sahteleniyor: burada test edilen sey veri okumak degil,
# LLM ciktisinin sorgu agacina nasil cevrildigi. Agir bagimliliklari kurmadan
# calisabilmesi icin.

# Her iki bagimlilik da kosulsuz sahteleniyor. Gercek sqlalchemy kurulu olsa
# bile `create_engine("stub://")` bilinmeyen lehce diye duserdi; testin amaci
# baglanmak degil, uretilen sorgu agacini gormek. Kosulsuz sahtelemek testi
# her makinede ayni sekilde calistirir.
_pandas = types.ModuleType("pandas")
_pandas.DataFrame = type("DataFrame", (), {})
_pandas.isna = lambda v: v is None
_pandas.read_sql = lambda *a, **k: None
sys.modules["pandas"] = _pandas

_sqlalchemy = types.ModuleType("sqlalchemy")
_sqlalchemy.create_engine = lambda *a, **k: object()
_sqlalchemy.text = lambda s: s
sys.modules["sqlalchemy"] = _sqlalchemy

from agent_analyzer import AgentAnalyzer  # noqa: E402

analyzer = AgentAnalyzer("stub://")

check("şemalı tablo adı", analyzer._split_table("dbo.Shipments"), ("dbo", "Shipments"))
check("şemasız tablo adı dbo sayılır", analyzer._split_table("Shipments"), ("dbo", "Shipments"))
check("köşeli parantezli tablo adı", analyzer._split_table("[sales].[Orders]"), ("sales", "Orders"))

check("metin limit", analyzer._safe_limit("7"), 7)
check("bozuk limit varsayılana düşer", analyzer._safe_limit("abc"), 10)
check("negatif limit kelepçelenir", analyzer._safe_limit(-3), 1)
check("aşırı limit kelepçelenir", analyzer._safe_limit(999999), 1000)

columns = {"ulke": "Ulke", "tutar": "Tutar", "sevktarihi": "SevkTarihi"}
check("kolon adı büyük/küçük harf duyarsız", analyzer._resolve_column("ULKE", columns), "Ulke")
check("köşeli parantezli kolon adı", analyzer._resolve_column("[Tutar]", columns), "Tutar")
check("olmayan kolon None döner", analyzer._resolve_column("Yok", columns), None)

predicates, params, _ = analyzer._build_where(
    {"SevkTarihi": {"gte": "2026-01-01", "lt": "2027-01-01"},
     "Ulke": ["Almanya", "Hollanda"]},
    columns, "t0")

check(
    "aralık ve liste filtreleri",
    " AND ".join(p.sql() for p in predicates),
    "[t0].[SevkTarihi] >= :p0 AND [t0].[SevkTarihi] < :p1 "
    "AND [t0].[Ulke] IN (:p2, :p3)",
)
check(
    "değerler yalnızca parametre sözlüğünde",
    params,
    {"p0": "2026-01-01", "p1": "2027-01-01", "p2": "Almanya", "p3": "Hollanda"},
)

null_predicates, null_params, _ = analyzer._build_where({"Ulke": None}, columns, "t0")
check("boş değer filtresi", null_predicates[0].sql(), "[t0].[Ulke] IS NULL")
check("IS NULL parametre üretmez", null_params, {})

expect_error(
    "olmayan filtre kolonu sessizce düşmez",
    lambda: analyzer._build_where({"Olmayan": 1}, columns, "t0"),
    "Mevcut kolonlar", ValueError,
)
expect_error(
    "tanınmayan karşılaştırma sessizce düşmez",
    lambda: analyzer._build_where({"Tutar": {"yaklasik": 5}}, columns, "t0"),
    "tanınmayan karşılaştırma", ValueError,
)

# main.py hata sebebini `error` alanindan okuyor; bu alan kaybolursa butun
# hatalar kullaniciya yeniden "Analysis failed" diye gorunur.
failure = AgentAnalyzer._error_result("kolon yok")
check("hata sonucu error alanı taşır", failure.get("error"), "kolon yok")
check("hata sonucu success=False", failure.get("success"), False)


print("\n\n".join(FAILS) if FAILS else "TÜM TESTLER GEÇTİ")
sys.exit(1 if FAILS else 0)
