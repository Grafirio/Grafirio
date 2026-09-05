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
    Aggregate, AliasFactory, ColumnRef, DerivedTable, Join, JoinCondition,
    OrderBy, Predicate, QuerySpec, QuerySpecError, TableRef, render,
    render_group_count, MANY_TO_ONE, ONE_TO_MANY,
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


# ── Bolum 1b: Zincir, tekrarli join ve on toplama ─────────────────────────
#
# Bu bolumdeki her sey tek tabloda hic ortaya cikmayan, yalnizca coklu tablo
# geldiginde yanlis cevap ureten durumlar.

chain_aliases = AliasFactory()
inv = TableRef("dbo", "Faturalar", chain_aliases.take())          # t0
cust = TableRef("dbo", "Musteriler", chain_aliases.take())        # t1
land = TableRef("dbo", "Ulkeler", chain_aliases.take())           # t2

# Zincir: Faturalar → Musteriler → Ulkeler. Ikinci join birinciye baglaniyor.
chain = QuerySpec(
    base=inv,
    joins=[
        Join(cust, [JoinCondition(ColumnRef(inv.alias, "MusteriId"),
                                  ColumnRef(cust.alias, "Id"))]),
        Join(land, [JoinCondition(ColumnRef(cust.alias, "UlkeKodu"),
                                  ColumnRef(land.alias, "Kod"))]),
    ],
    group_by=[ColumnRef(land.alias, "Ad")],
    aggregate=Aggregate("count", None, "value"),
    order_by=[OrderBy("value", "desc"), OrderBy(ColumnRef(land.alias, "Ad"), "asc")],
    limit=5,
)

check(
    "zincirleme join sırayla üretilir",
    render(chain),
    "SELECT TOP (5) [t2].[Ad], COUNT(*) AS [value] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "LEFT JOIN [dbo].[Musteriler] AS [t1] ON [t0].[MusteriId] = [t1].[Id] "
    "LEFT JOIN [dbo].[Ulkeler] AS [t2] ON [t1].[UlkeKodu] = [t2].[Kod] "
    "GROUP BY [t2].[Ad] "
    "ORDER BY [value] DESC, [t2].[Ad] ASC",
)

# Ileri referans: ilk join, henuz acilmamis t2'ye atif yapiyor. Eskiden
# takma adlar tek kume olarak toplandigi icin bu dogrulamadan geciyordu.
expect_error(
    "ileri referans reddedilir",
    lambda: render(QuerySpec(
        base=inv,
        joins=[
            Join(cust, [JoinCondition(ColumnRef(land.alias, "Kod"),
                                      ColumnRef(cust.alias, "UlkeKodu"))]),
            Join(land, [JoinCondition(ColumnRef(cust.alias, "UlkeKodu"),
                                      ColumnRef(land.alias, "Kod"))]),
        ],
        select_all=True)),
    "bu noktada tanımlı değil", QuerySpecError,
)

expect_error(
    "aynı takma ad iki kez kullanılamaz",
    lambda: render(QuerySpec(
        base=inv,
        joins=[
            Join(cust, [JoinCondition(ColumnRef(inv.alias, "MusteriId"),
                                      ColumnRef(cust.alias, "Id"))]),
            Join(TableRef("dbo", "Baska", cust.alias),
                 [JoinCondition(ColumnRef(inv.alias, "X"),
                                ColumnRef(cust.alias, "Y"))]),
        ],
        select_all=True)),
    "iki kez kullanılmış", QuerySpecError,
)

# Ayni parametre tablosuna iki kez: ON'daki sabit kosul ikisini ayiriyor.
par1 = TableRef("dbo", "Parametreler", "t5")
par2 = TableRef("dbo", "Parametreler", "t6")
double_lookup = QuerySpec(
    base=inv,
    joins=[
        Join(par1, [
            JoinCondition(ColumnRef(inv.alias, "ParaBirimiKodu"),
                          ColumnRef(par1.alias, "Kod")),
            Predicate(ColumnRef(par1.alias, "Tip"), "=", ["p_cur"]),
        ]),
        Join(par2, [
            JoinCondition(ColumnRef(inv.alias, "OdemeTipiKodu"),
                          ColumnRef(par2.alias, "Kod")),
            Predicate(ColumnRef(par2.alias, "Tip"), "=", ["p_pay"]),
        ]),
    ],
    select_all=True,
)

check(
    "aynı tabloya iki join, sabit koşulla ayrışır",
    render(double_lookup),
    "SELECT [t0].* FROM [dbo].[Faturalar] AS [t0] "
    "LEFT JOIN [dbo].[Parametreler] AS [t5] "
    "ON [t0].[ParaBirimiKodu] = [t5].[Kod] AND [t5].[Tip] = :p_cur "
    "LEFT JOIN [dbo].[Parametreler] AS [t6] "
    "ON [t0].[OdemeTipiKodu] = [t6].[Kod] AND [t6].[Tip] = :p_pay",
)

if ":p_cur" not in render(double_lookup) or "'" in render(double_lookup):
    FAILS.append("ON koşulundaki değer metne gömülmüş")

# On toplama: kalemler fatura basina toplanip N:1 baglaniyor.
lines = DerivedTable(
    source=TableRef("dbo", "FaturaKalemleri", "k0"),
    key_columns=[ColumnRef("k0", "FaturaId")],
    aggregate=Aggregate("sum", ColumnRef("k0", "Tutar"), "kalem_toplam"),
    alias="d0",
)
pre_agg = QuerySpec(
    base=inv,
    joins=[Join(lines, [JoinCondition(ColumnRef(inv.alias, "Id"),
                                      ColumnRef("d0", "FaturaId"))])],
    group_by=[ColumnRef(inv.alias, "Ulke")],
    aggregate=Aggregate("sum", ColumnRef("d0", "kalem_toplam"), "value"),
    order_by=[OrderBy("value", "desc"), OrderBy(ColumnRef(inv.alias, "Ulke"), "asc")],
    limit=5,
)

check(
    "ön toplanmış alt sorgu N:1 bağlanır",
    render(pre_agg),
    "SELECT TOP (5) [t0].[Ulke], SUM([d0].[kalem_toplam]) AS [value] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "LEFT JOIN (SELECT [k0].[FaturaId], SUM([k0].[Tutar]) AS [kalem_toplam] "
    "FROM [dbo].[FaturaKalemleri] AS [k0] "
    "GROUP BY [k0].[FaturaId]) AS [d0] ON [t0].[Id] = [d0].[FaturaId] "
    "GROUP BY [t0].[Ulke] "
    "ORDER BY [value] DESC, [t0].[Ulke] ASC",
)

# Ham 1:N hâlâ reddediliyor — ve mesaj cozumu soyluyor.
expect_error(
    "ham 1:N join toplulaştırmada reddedilir",
    lambda: render(QuerySpec(
        base=inv,
        joins=[Join(TableRef("dbo", "FaturaKalemleri", "t9"),
                    [JoinCondition(ColumnRef(inv.alias, "Id"),
                                   ColumnRef("t9", "FaturaId"))],
                    cardinality=ONE_TO_MANY)],
        group_by=[ColumnRef(inv.alias, "Ulke")],
        aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value"), OrderBy(ColumnRef(inv.alias, "Ulke"))],
        limit=5)),
    "ön toplanmış hâliyle", QuerySpecError,
)

# Anahtarin tamami uzerinden baglanmazsa teklik garantisi coker.
two_key = DerivedTable(
    source=TableRef("dbo", "Kalemler", "k1"),
    key_columns=[ColumnRef("k1", "FaturaId"), ColumnRef("k1", "Donem")],
    aggregate=Aggregate("sum", ColumnRef("k1", "Tutar"), "toplam"),
    alias="d1",
)
expect_error(
    "eksik anahtarla bağlanan ön toplama reddedilir",
    lambda: render(QuerySpec(
        base=inv,
        joins=[Join(two_key, [JoinCondition(ColumnRef(inv.alias, "Id"),
                                            ColumnRef("d1", "FaturaId"))])],
        select_all=True)),
    "Donem", QuerySpecError,
)

expect_error(
    "alt sorguda olmayan kolona atıf reddedilir",
    lambda: render(QuerySpec(
        base=inv,
        joins=[Join(lines, [JoinCondition(ColumnRef(inv.alias, "Id"),
                                          ColumnRef("d0", "FaturaId"))])],
        group_by=[ColumnRef("d0", "Aciklama")],
        aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value"), OrderBy(ColumnRef("d0", "Aciklama"))],
        limit=5)),
    "ön toplanmış", QuerySpecError,
)

expect_error(
    "alt sorgu dışarıdaki takma adı göremez",
    lambda: render(QuerySpec(
        base=inv,
        joins=[Join(DerivedTable(
            source=TableRef("dbo", "Kalemler", "k2"),
            key_columns=[ColumnRef("k2", "FaturaId")],
            aggregate=Aggregate("sum", ColumnRef(inv.alias, "Tutar"), "toplam"),
            alias="d2",
        ), [JoinCondition(ColumnRef(inv.alias, "Id"),
                          ColumnRef("d2", "FaturaId"))])],
        select_all=True)),
    "yalnızca kendi tablosunu görür", QuerySpecError,
)


# ── Bolum 2: AgentAnalyzer'in DB gerektirmeyen mantigi ────────────────────
#
# pandas sahteleniyor: burada test edilen sey veri okumak degil, LLM
# ciktisinin sorgu agacina nasil cevrildigi. Agir bagimliligi kurmadan
# calisabilmesi icin.
#
# sqlalchemy artik sahtelenmiyor cunku AgentAnalyzer ona hic dokunmuyor:
# veriye `DataPort` uzerinden gidiyor ve o kapinin dogrudan-baglanti
# uygulamasi sqlalchemy'yi ancak kendi kurucusunda iceri aliyor.
_pandas = types.ModuleType("pandas")
_pandas.DataFrame = type("DataFrame", (), {})
_pandas.isna = lambda v: v is None
_pandas.read_sql = lambda *a, **k: None
from unittest.mock import patch
with patch.dict(sys.modules, {"pandas": _pandas}):
    from agent_analyzer import AgentAnalyzer, ColumnScope


class _StubDataPort:
    """Sorgu calistirmayan kapi: bu bolumde veriye hic gidilmiyor."""

    def read_sql(self, sql, params=None, max_rows=None):
        raise AssertionError(f"beklenmeyen sorgu: {sql}")

    def scalar(self, sql, params=None):
        raise AssertionError(f"beklenmeyen sorgu: {sql}")


analyzer = AgentAnalyzer(_StubDataPort())

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

base_scope = ColumnScope()
base_scope.add("t0", columns, ["dbo.Shipments", "Shipments"], is_base=True)

# Cok tablolu kapsam: ayni kolon adi iki tabloda.
multi = ColumnScope()
multi.add("t0", {"ad": "Ad", "musteriid": "MusteriId"}, ["dbo.Faturalar", "Faturalar"], is_base=True)
multi.add("t1", {"ad": "Ad", "id": "Id"}, ["musteri", "dbo.Musteriler", "Musteriler"])

check("çıplak ad önce tabana çözülür", multi.resolve("Ad"), ColumnRef("t0", "Ad"))
check("nitelenmiş ad ilgili tabloya gider", multi.resolve("musteri.Ad"), ColumnRef("t1", "Ad"))
check("şemalı nitelenmiş ad", multi.resolve("dbo.Musteriler.Ad"), ColumnRef("t1", "Ad"))
check("yalnız bağlı tabloda olan ad bulunur", multi.resolve("Id"), ColumnRef("t1", "Id"))
check("olmayan kolon None", multi.resolve("Yok"), None)

ambiguous = ColumnScope()
ambiguous.add("t1", {"kod": "Kod"}, ["a"])
ambiguous.add("t2", {"kod": "Kod"}, ["b"])
expect_error(
    "belirsiz kolon adı tahmin edilmez",
    lambda: ambiguous.resolve("Kod"),
    "birden fazla tabloda", ValueError,
)

predicates, params, _ = analyzer._build_where(
    {"SevkTarihi": {"gte": "2026-01-01", "lt": "2027-01-01"},
     "Ulke": ["Almanya", "Hollanda"]},
    base_scope)

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

null_predicates, null_params, _ = analyzer._build_where({"Ulke": None}, base_scope)
check("boş değer filtresi", null_predicates[0].sql(), "[t0].[Ulke] IS NULL")
check("IS NULL parametre üretmez", null_params, {})

expect_error(
    "olmayan filtre kolonu sessizce düşmez",
    lambda: analyzer._build_where({"Olmayan": 1}, base_scope),
    "Mevcut kolonlar", ValueError,
)
expect_error(
    "tanınmayan karşılaştırma sessizce düşmez",
    lambda: analyzer._build_where({"Tutar": {"yaklasik": 5}}, base_scope),
    "tanınmayan karşılaştırma", ValueError,
)

# ── Bolum 3: Join cozumleyici ─────────────────────────────────────────────
#
# Buradaki degismez su: kardinalite, eslesen kolonlar ve join tipi MODELDEN
# gelmez, olculmus iliski kaydindan okunur. Model yalnizca hangi tabloyu
# nereye baglamak istedigini soyler. Bu ayrim bozulursa 1:N emniyeti anlamini
# yitirir — model kontrolden gecmek icin "many-to-one" yazar ve sayilar
# sessizce siser.

# pandas yukarida sahtelendigi icin gercek DataFrame yok; `_table_columns`in
# kullandigi kadarini (empty, iloc[:, 0].tolist()) tasiyan bir sahte yeterli.
class _FakeFrame:
    def __init__(self, values):
        self._values = list(values)
        self.empty = not self._values
        self.iloc = self

    def __getitem__(self, key):
        return self

    def tolist(self):
        return list(self._values)


class _SchemaPort:
    """INFORMATION_SCHEMA sorgusuna tablo basina kolon listesi doner."""

    TABLES = {
        "Faturalar": ["Id", "MusteriId", "Ulke", "Tutar", "ParaBirimiKodu"],
        "Musteriler": ["Id", "Ad", "UlkeKodu"],
        "Ulkeler": ["Kod", "Ad"],
        "FaturaKalemleri": ["Id", "FaturaId", "Tutar", "Adet"],
    }

    def read_sql(self, sql, params=None, max_rows=None):
        return _FakeFrame(self.TABLES[(params or {})["table"]])

    def scalar(self, sql, params=None):
        raise AssertionError("beklenmeyen sorgu")


def edge(frm, frm_cols, to, to_cols, **extra):
    e = {"fromTable": frm, "fromColumns": frm_cols,
         "toTable": to, "toColumns": to_cols,
         "cardinality": "many-to-one", "source": "fk",
         "isTrusted": False, "isOptional": True}
    e.update(extra)
    return e


EDGES = [
    edge("dbo.Faturalar", ["MusteriId"], "dbo.Musteriler", ["Id"]),
    edge("dbo.Musteriler", ["UlkeKodu"], "dbo.Ulkeler", ["Kod"]),
    edge("dbo.FaturaKalemleri", ["FaturaId"], "dbo.Faturalar", ["Id"]),
]

joiner = AgentAnalyzer(_SchemaPort())
from analysis_scope import AnalysisScope


def selected_schema(tables):
    return AnalysisScope({
        "tables": [{"name": f"dbo.{table}"} for table in tables],
        "columns": [{"table": f"dbo.{table}", "column": column}
                    for table, columns in tables.items() for column in columns],
    })


joiner._selection = selected_schema(_SchemaPort.TABLES)


def resolve(requested, edges=EDGES):
    """Cozumleyiciyi calistirir; (joins, notlar, kapsam) doner."""
    als = AliasFactory()
    root = TableRef("dbo", "Faturalar", als.take())
    sc = ColumnScope()
    sc.add(root.alias, {c.lower(): c for c in _SchemaPort.TABLES["Faturalar"]},
           ["dbo.Faturalar", "Faturalar"], is_base=True)
    js, _p, notes = joiner._resolve_joins(
        {"relationships": edges}, root, "dbo.Faturalar", requested, als, sc)
    return js, notes, sc, root


# N:1: dogrudan baglanir, kolonlar olculmus kayittan gelir.
js, _n, _s, root = resolve([{"as": "musteri", "from": "base", "table": "dbo.Musteriler"}])
check("N:1 join tek adım üretir", len(js), 1)
check(
    "eşleşme kolonları ölçülmüş kayıttan okunur",
    js[0].sql(),
    "LEFT JOIN [dbo].[Musteriler] AS [t1] ON [t0].[MusteriId] = [t1].[Id]",
)

# Guvenilir ve zorunlu FK: INNER guvenli.
trusted_edges = [edge("dbo.Faturalar", ["MusteriId"], "dbo.Musteriler", ["Id"],
                      isTrusted=True, isOptional=False)]
js, _n, _s, _r = resolve([{"as": "m", "from": "base", "table": "dbo.Musteriler"}],
                         trusted_edges)
check("doğrulanmış zorunlu FK INNER olur", js[0].kind, "inner")

# Cikarsanmis kenar: LEFT kalmali, aksi halde eslesmeyen satirlar sessizce duser.
inferred = [edge("dbo.Faturalar", ["MusteriId"], "dbo.Musteriler", ["Id"],
                 source="inferred", isTrusted=True, isOptional=False,
                 valueOverlap=0.87)]
js, notes, _s, _r = resolve([{"as": "m", "from": "base", "table": "dbo.Musteriler"}],
                            inferred)
check("çıkarsanmış kenar INNER olmaz", js[0].kind, "left")
if "örtüşme %87" not in " ".join(notes):
    FAILS.append("çıkarsanmış kenarın örtüşme oranı denetim izine yazılmamış")

# Zincir: ikinci adim birinciye baglaniyor.
js, _n, sc, _r = resolve([
    {"as": "musteri", "from": "base", "table": "dbo.Musteriler"},
    {"as": "ulke", "from": "musteri", "table": "dbo.Ulkeler"},
])
check("zincir iki join üretir", len(js), 2)
check(
    "ikinci adım birinciye bağlanır",
    js[1].sql(),
    "LEFT JOIN [dbo].[Ulkeler] AS [t2] ON [t1].[UlkeKodu] = [t2].[Kod]",
)
check("bağlanan tablonun kolonu adıyla çözülür",
      sc.resolve("ulke.Ad"), ColumnRef("t2", "Ad"))

# Bire-cok: olcu belirtilmemis -> anahtar basina sayim.
js, notes, sc, _r = resolve([{"as": "kalem", "from": "base",
                              "table": "dbo.FaturaKalemleri"}])
check("bire-çok tablo ön toplanır", isinstance(js[0].table, DerivedTable), True)
check(
    "ölçü verilmeyince kayıt sayısı toplanır",
    js[0].sql(),
    "LEFT JOIN (SELECT [t1].[FaturaId], COUNT(*) AS [kalem_count] "
    "FROM [dbo].[FaturaKalemleri] AS [t1] "
    "GROUP BY [t1].[FaturaId]) AS [t2] ON [t0].[Id] = [t2].[FaturaId]",
)

# Bire-cok: olcu belirtilmis -> o olcu on toplanir.
js, _n, sc, _r = resolve([{"as": "kalem", "from": "base",
                           "table": "dbo.FaturaKalemleri",
                           "preAggregate": {"aggregation": "sum", "column": "Tutar"}}])
check(
    "istenen ölçü ön toplanır",
    js[0].sql(),
    "LEFT JOIN (SELECT [t1].[FaturaId], SUM([t1].[Tutar]) AS [kalem_sum] "
    "FROM [dbo].[FaturaKalemleri] AS [t1] "
    "GROUP BY [t1].[FaturaId]) AS [t2] ON [t0].[Id] = [t2].[FaturaId]",
)
check("ön toplanmış sonuç adıyla çözülür",
      sc.resolve("kalem.kalem_sum"), ColumnRef("t2", "kalem_sum"))
check("ön toplanmış tablonun ham kolonu görünmez",
      sc.resolve("kalem.Adet"), None)

expect_error(
    "olmayan bağlantı uydurulmaz",
    lambda: resolve([{"as": "u", "from": "base", "table": "dbo.Ulkeler"}]),
    "ölçülmüş bir bağlantı yok", ValueError,
)

expect_error(
    "olmayan adıma bağlanılamaz",
    lambda: resolve([{"as": "u", "from": "yok", "table": "dbo.Musteriler"}]),
    "böyle bir adım yok", ValueError,
)

# Iki tablo arasinda birden fazla kenar: tahmin yok, `via` sart.
two_edges = [
    edge("dbo.Faturalar", ["MusteriId"], "dbo.Musteriler", ["Id"]),
    edge("dbo.Faturalar", ["ParaBirimiKodu"], "dbo.Musteriler", ["Id"]),
]
expect_error(
    "birden fazla bağlantı varsa tahmin edilmez",
    lambda: resolve([{"as": "m", "from": "base", "table": "dbo.Musteriler"}], two_edges),
    "birden fazla bağlantı var", ValueError,
)
js, _n, _s, _r = resolve(
    [{"as": "m", "from": "base", "table": "dbo.Musteriler", "via": "ParaBirimiKodu"}],
    two_edges)
check("via ile belirsizlik çözülür",
      js[0].sql(),
      "LEFT JOIN [dbo].[Musteriler] AS [t1] ON [t0].[ParaBirimiKodu] = [t1].[Id]")

expect_error(
    "zincir üst sınırı aşılamaz",
    lambda: resolve([{"as": f"a{i}", "from": "base", "table": "dbo.Musteriler"}
                     for i in range(AgentAnalyzer.MAX_JOINS + 1)]),
    "en fazla", ValueError,
)

expect_error(
    "aynı ad zincirde iki kez kullanılamaz",
    lambda: resolve([
        {"as": "m", "from": "base", "table": "dbo.Musteriler"},
        {"as": "m", "from": "m", "table": "dbo.Ulkeler"},
    ]),
    "iki kez kullanılmış", ValueError,
)

expect_error(
    "bağlantılar çıkarılmamışsa açıkça söylenir",
    lambda: resolve([{"as": "m", "from": "base", "table": "dbo.Musteriler"}], []),
    "Analiz Et", ValueError,
)

# Ayni parametre tablosuna iki kez: `filter` ikisini ayiriyor. Bu desende
# ayirt edici kosul ON'a girmeli — WHERE'e konsa LEFT join sessizce INNER'a
# doner ve eslesmeyen faturalar sonuctan duserdi.
_SchemaPort.TABLES["Parametreler"] = ["Kod", "Tip", "Ad"]
joiner._selection = selected_schema(_SchemaPort.TABLES)
param_edges = [
    edge("dbo.Faturalar", ["ParaBirimiKodu"], "dbo.Parametreler", ["Kod"]),
    edge("dbo.Faturalar", ["Ulke"], "dbo.Parametreler", ["Kod"]),
]

als = AliasFactory()
root = TableRef("dbo", "Faturalar", als.take())
sc = ColumnScope()
sc.add(root.alias, {c.lower(): c for c in _SchemaPort.TABLES["Faturalar"]},
       ["dbo.Faturalar", "Faturalar"], is_base=True)
js, jp, _n = joiner._resolve_joins(
    {"relationships": param_edges}, root, "dbo.Faturalar",
    [{"as": "parabirimi", "from": "base", "table": "dbo.Parametreler",
      "via": "ParaBirimiKodu", "filter": {"Tip": "CUR"}},
     {"as": "ulkeadi", "from": "base", "table": "dbo.Parametreler",
      "via": "Ulke", "filter": {"Tip": "CNT"}}],
    als, sc)

check(
    "aynı tabloya iki join ayrı takma ad alır",
    [j.table.alias for j in js],
    ["t1", "t2"],
)
check(
    "ayırt edici koşul ON'a girer",
    js[0].sql(),
    "LEFT JOIN [dbo].[Parametreler] AS [t1] "
    "ON [t0].[ParaBirimiKodu] = [t1].[Kod] AND [t1].[Tip] = :j0_0",
)
check("koşul değeri parametreye bağlanır", jp, {"j0_0": "CUR", "j1_0": "CNT"})
check("iki bağlantı ayrı adlarla çözülür",
      (sc.resolve("parabirimi.Ad"), sc.resolve("ulkeadi.Ad")),
      (ColumnRef("t1", "Ad"), ColumnRef("t2", "Ad")))

# Uretilen sorgu bir butun olarak da gecerli olmali.
check(
    "iki kez bağlanan tablo geçerli SQL üretir",
    render(QuerySpec(
        base=root, joins=js,
        group_by=[ColumnRef("t1", "Ad")],
        aggregate=Aggregate("count", None, "value"),
        order_by=[OrderBy("value", "desc"), OrderBy(ColumnRef("t1", "Ad"), "asc")],
        limit=5)),
    "SELECT TOP (5) [t1].[Ad], COUNT(*) AS [value] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "LEFT JOIN [dbo].[Parametreler] AS [t1] "
    "ON [t0].[ParaBirimiKodu] = [t1].[Kod] AND [t1].[Tip] = :j0_0 "
    "LEFT JOIN [dbo].[Parametreler] AS [t2] "
    "ON [t0].[Ulke] = [t2].[Kod] AND [t2].[Tip] = :j1_0 "
    "GROUP BY [t1].[Ad] "
    "ORDER BY [value] DESC, [t1].[Ad] ASC",
)

expect_error(
    "ön toplanan tabloya ayırt edici koşul verilemez",
    lambda: resolve([{"as": "kalem", "from": "base",
                      "table": "dbo.FaturaKalemleri",
                      "filter": {"Adet": 1}}]),
    "ayırt edici koşul veremiyorum", ValueError,
)

expect_error(
    "olmayan kolona birleştirme koşulu verilemez",
    lambda: joiner._build_join_filter({"Yok": 1}, {"tip": "Tip"}, "t1", 0),
    "bağlanan tabloda yok", ValueError,
)


# main.py hata sebebini `error` alanindan okuyor; bu alan kaybolursa butun
# hatalar kullaniciya yeniden "Analysis failed" diye gorunur.
failure = AgentAnalyzer._error_result("kolon yok")
check("hata sonucu error alanı taşır", failure.get("error"), "kolon yok")
check("hata sonucu success=False", failure.get("success"), False)


# ── Bolum 4: Toplulastirma sonrasi kosul, pencere ve birlesim ─────────────
#
# Ucu de "calisan ama baska bir soruyu cevaplayan sorgu" uretmeye acik:
#
#   * HAVING ile WHERE karistirilirsa sorgu calisir, sonuc baskadir.
#   * Sirali bir birikim penceresine cerceve verilmezse esit degerler tek
#     satirda toplanir ve grafikte fark edilmez.
#   * Birlesimin dallari farkli kolonlar uretirse veritabani onlari sirayla
#     eslestirir ve seriler birbirine karisir.

from query_spec import (  # noqa: E402
    FRAME_CUMULATIVE, FRAME_MOVING, HavingPredicate, UnionBranch, UnionSpec,
    WindowExpr, render_union,
)

agg4 = AliasFactory()
fatura = TableRef("dbo", "Faturalar", agg4.take())
musteri_adi = ColumnRef(fatura.alias, "MusteriAdi")
tutar = ColumnRef(fatura.alias, "Tutar")
toplam = Aggregate("sum", tutar, "value")

having_spec = QuerySpec(
    base=fatura,
    group_by=[musteri_adi],
    aggregate=toplam,
    having=[HavingPredicate(toplam, ">", ["h0"])],
    order_by=[OrderBy("value", "desc")],
)

check(
    "HAVING gruplamadan sonra, sıralamadan önce",
    render(having_spec),
    "SELECT [t0].[MusteriAdi], SUM([t0].[Tutar]) AS [value] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "GROUP BY [t0].[MusteriAdi] "
    "HAVING SUM([t0].[Tutar]) > :h0 "
    "ORDER BY [value] DESC",
)

# Elenen gruplari da sayarsak "5 grup gosteriliyor (toplam 37)" yalan olur.
check(
    "grup sayımı HAVING'i de uygular",
    "HAVING SUM([t0].[Tutar]) > :h0" in render_group_count(having_spec),
    True,
)

check(
    "global aggregate supports HAVING without a fabricated group",
    render(QuerySpec(base=fatura, aggregate=toplam,
                     having=[HavingPredicate(toplam, ">", ["h0"])])),
    "SELECT SUM([t0].[Tutar]) AS [value] FROM [dbo].[Faturalar] AS [t0] "
    "HAVING SUM([t0].[Tutar]) > :h0",
)

expect_error(
    "toplulaştırmasız sorguda HAVING olmaz",
    lambda: render(QuerySpec(base=fatura, select_all=True,
                             having=[HavingPredicate(toplam, ">", ["h0"])])),
    "Toplulaştırması olmayan sorguda HAVING", QuerySpecError,
)

# Pencere fonksiyonlari.
ay = ColumnRef(fatura.alias, "Ay")

check(
    "birikimli toplam iç içe toplulaştırma üretir",
    render(QuerySpec(
        base=fatura, group_by=[ay], aggregate=toplam,
        windows=[WindowExpr(func="sum", label="birikim", over=toplam,
                            order_by=[OrderBy(ay, "asc")],
                            frame=FRAME_CUMULATIVE)])),
    "SELECT [t0].[Ay], SUM([t0].[Tutar]) AS [value], "
    "SUM(SUM([t0].[Tutar])) OVER (ORDER BY [t0].[Ay] ASC "
    "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS [birikim] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "GROUP BY [t0].[Ay]",
)

check(
    "sıra numarası ölçünün kendisine göre sıralanır",
    render(QuerySpec(
        base=fatura, group_by=[musteri_adi], aggregate=toplam,
        windows=[WindowExpr(func="rank", label="sira",
                            order_by=[OrderBy(toplam, "desc")])])),
    "SELECT [t0].[MusteriAdi], SUM([t0].[Tutar]) AS [value], "
    "RANK() OVER (ORDER BY SUM([t0].[Tutar]) DESC) AS [sira] "
    "FROM [dbo].[Faturalar] AS [t0] "
    "GROUP BY [t0].[MusteriAdi]",
)

check(
    "hareketli ortalama kaç satır geriye bakacağını yazar",
    "ROWS BETWEEN 3 PRECEDING AND CURRENT ROW" in render(QuerySpec(
        base=fatura, group_by=[ay], aggregate=toplam,
        windows=[WindowExpr(func="avg", label="ortalama", over=toplam,
                            order_by=[OrderBy(ay, "asc")],
                            frame=FRAME_MOVING, preceding=3)])),
    True,
)

# Bu, bolumun asil sebebi: cerceve yazilmazsa SQL varsayilani birikim gibi
# gorunur ama esit siralama degerlerini tek satirda toplar.
expect_error(
    "sıralı birikim penceresinde çerçeve zorunlu",
    lambda: render(QuerySpec(
        base=fatura, group_by=[ay], aggregate=toplam,
        windows=[WindowExpr(func="sum", label="birikim", over=toplam,
                            order_by=[OrderBy(ay, "asc")])])),
    "çerçeve açıkça", QuerySpecError,
)

expect_error(
    "hareketli pencere satır sayısı ister",
    lambda: render(QuerySpec(
        base=fatura, group_by=[ay], aggregate=toplam,
        windows=[WindowExpr(func="avg", label="ort", over=toplam,
                            order_by=[OrderBy(ay, "asc")],
                            frame=FRAME_MOVING)])),
    "kaç satır geriye bakılacağı", QuerySpecError,
)

expect_error(
    "pencere kırılımda olmayan kolona bakamaz",
    lambda: render(QuerySpec(
        base=fatura, group_by=[ay], aggregate=toplam,
        windows=[WindowExpr(func="sum", label="birikim", over=toplam,
                            partition_by=[musteri_adi],
                            order_by=[OrderBy(ay, "asc")],
                            frame=FRAME_CUMULATIVE)])),
    "kırılımda yok", QuerySpecError,
)

expect_error(
    "pencere etiketi ölçünün adını alamaz",
    lambda: render(QuerySpec(
        base=fatura, group_by=[musteri_adi], aggregate=toplam,
        windows=[WindowExpr(func="rank", label="value",
                            order_by=[OrderBy(toplam, "desc")])])),
    "sonuçta zaten var", QuerySpecError,
)

expect_error(
    "sıralama fonksiyonu sırasız olamaz",
    lambda: render(QuerySpec(
        base=fatura, group_by=[musteri_adi], aggregate=toplam,
        windows=[WindowExpr(func="row_number", label="sira")])),
    "sıralama olmadan anlamsız", QuerySpecError,
)

expect_error(
    "satır bazlı sorguda pencere olmaz",
    lambda: render(QuerySpec(
        base=fatura, select_all=True,
        windows=[WindowExpr(func="row_number", label="sira",
                            order_by=[OrderBy(ay, "asc")])])),
    "yalnızca toplulaştırmalı sorguda", QuerySpecError,
)

expect_error(
    "pencere içinde çıktı adına göre sıralanamaz",
    lambda: render(QuerySpec(
        base=fatura, group_by=[musteri_adi], aggregate=toplam,
        windows=[WindowExpr(func="rank", label="sira",
                            order_by=[OrderBy("value", "desc")])])),
    "pencere içinde takma ad görünmez", QuerySpecError,
)

# Birlesim.
uni = AliasFactory()
ithalat = TableRef("dbo", "Ithalat", uni.take())
ihracat = TableRef("dbo", "Ihracat", uni.take())


def union_of(**overrides):
    """İki dallı birleşim; testler yalnızca değiştirdikleri alanı verir."""
    branches = overrides.pop("branches", None)
    if branches is None:
        branches = [
            UnionBranch(QuerySpec(
                base=ithalat,
                group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
                aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value"),
            ), "src0"),
            UnionBranch(QuerySpec(
                base=ihracat,
                # Iki tablo ayni seyi farkli adla tutuyor: etiket olmadan
                # birlesimin cikti adlari tutmaz.
                group_by=[ColumnRef(ihracat.alias, "Country", label="UlkeAdi")],
                aggregate=Aggregate("sum", ColumnRef(ihracat.alias, "Amount"), "value"),
            ), "src1"),
        ]
    spec = UnionSpec(branches=branches, **overrides)
    return spec


check(
    "birleşim dalları alt alta eklenir ve kaynağını söyler",
    render_union(union_of(order_by=[OrderBy("value", "desc")], limit=10)),
    "SELECT TOP (10) * FROM ("
    "SELECT [t0].[UlkeAdi], SUM([t0].[Tutar]) AS [value], :src0 AS [Kaynak] "
    "FROM [dbo].[Ithalat] AS [t0] GROUP BY [t0].[UlkeAdi]"
    " UNION ALL "
    "SELECT [t1].[Country] AS [UlkeAdi], SUM([t1].[Amount]) AS [value], :src1 AS [Kaynak] "
    "FROM [dbo].[Ihracat] AS [t1] GROUP BY [t1].[Country]"
    ") AS [u] ORDER BY [value] DESC",
)

expect_error(
    "dalların çıktı kolonları tutmalı",
    lambda: render_union(union_of(branches=[
        UnionBranch(QuerySpec(
            base=ithalat, group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value")), "s0"),
        UnionBranch(QuerySpec(
            base=ihracat, group_by=[ColumnRef(ihracat.alias, "Country")],
            aggregate=Aggregate("sum", ColumnRef(ihracat.alias, "Amount"), "value")), "s1"),
    ])),
    "çıktı kolonları diğerlerinden farklı", QuerySpecError,
)

expect_error(
    "aynı parametre adı iki dalda kullanılamaz",
    lambda: render_union(union_of(branches=[
        UnionBranch(QuerySpec(
            base=ithalat, group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value"),
            where=[Predicate(ColumnRef(ithalat.alias, "Yil"), "=", ["p0"])]), "s0"),
        UnionBranch(QuerySpec(
            base=ihracat, group_by=[ColumnRef(ihracat.alias, "Country", label="UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ihracat.alias, "Amount"), "value"),
            where=[Predicate(ColumnRef(ihracat.alias, "Yil"), "=", ["p0"])]), "s1"),
    ])),
    "birden fazla dalda kullanılmış", QuerySpecError,
)

expect_error(
    "dalın kendi sıralaması olamaz",
    lambda: render_union(union_of(branches=[
        UnionBranch(QuerySpec(
            base=ithalat, group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value"),
            order_by=[OrderBy("value", "desc")]), "s0"),
        UnionBranch(QuerySpec(
            base=ihracat, group_by=[ColumnRef(ihracat.alias, "Country", label="UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ihracat.alias, "Amount"), "value")), "s1"),
    ])),
    "dalında sıralama ya da TOP olamaz", QuerySpecError,
)

expect_error(
    "sırasız birleşimde TOP olmaz",
    lambda: render_union(union_of(limit=5)),
    "Sıralaması olmayan bir birleşimde TOP", QuerySpecError,
)

expect_error(
    "birleşim kolon referansına göre sıralanamaz",
    lambda: render_union(union_of(
        order_by=[OrderBy(ColumnRef(ithalat.alias, "UlkeAdi"), "asc")])),
    "yalnızca çıktı adına göre", QuerySpecError,
)

expect_error(
    "tek dallı birleşim olmaz",
    lambda: render_union(UnionSpec(branches=[
        UnionBranch(QuerySpec(
            base=ithalat, group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value")), "s0"),
    ])),
    "en az iki dal", QuerySpecError,
)

expect_error(
    "kaynak etiketi ya hepsinde ya hiçbirinde",
    lambda: render_union(union_of(branches=[
        UnionBranch(QuerySpec(
            base=ithalat, group_by=[ColumnRef(ithalat.alias, "UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ithalat.alias, "Tutar"), "value")), "s0"),
        UnionBranch(QuerySpec(
            base=ihracat, group_by=[ColumnRef(ihracat.alias, "Country", label="UlkeAdi")],
            aggregate=Aggregate("sum", ColumnRef(ihracat.alias, "Amount"), "value"))),
    ])),
    "ya bütün dallarda olmalı ya hiçbirinde", QuerySpecError,
)


# ── Bolum 5: Model isteginin cozumlenmesi ─────────────────────────────────
#
# Model ham SQL yazmiyor, adlandirilmis kaliplar seciyor. Cozumleyici o
# kaliplari cerceveyi ve esigi kendisi kurarak `WindowExpr`/`HavingPredicate`e
# ceviriyor — modelin unutabilecegi bir sey kalmasin diye.

measure = Aggregate("sum", ColumnRef("t0", "Tutar"), "value")
group_ref = ColumnRef("t0", "Ay")

check("koşul yoksa hiçbir şey üretilmez",
      analyzer._build_having(None, measure), ([], {}, None))

hp, hparams, hnote = analyzer._build_having({"op": ">", "value": 1000}, measure)
check("eşik parametreye bağlanır", hparams, {"h0": 1000})
check("koşul ölçünün kendisine uygulanır", hp[0].sql(), "SUM([t0].[Tutar]) > :h0")
check("koşul denetim izine okunabilir yazılır", hnote, "SUM(Tutar) > 1000")

# Model bazen yalnizca sayiyi yaziyor; kastettigi neredeyse her zaman "bundan
# buyuk". Reddetmek yerine bunu okumak, sorunun cevapsiz kalmasindan iyi.
check("çıplak sayı 'bundan büyük' okunur",
      analyzer._build_having(1000, measure)[0][0].sql(), "SUM([t0].[Tutar]) > :h0")

expect_error(
    "koşul değeri metin olamaz",
    lambda: analyzer._build_having({"op": ">", "value": "çok"}, measure),
    "bir sayı olmalı", ValueError,
)

expect_error(
    "tanınmayan karşılaştırma reddedilir",
    lambda: analyzer._build_having({"op": "LIKE", "value": 1}, measure),
    "tanınmayan bir karşılaştırma", ValueError,
)

# ── İki ayrı ölçüye koşul ─────────────────────────────────────────────────
#
# "Cirosu 1 milyonu geçen AMA sipariş sayısı 5'ten az olan müşteriler" iki
# ayrı hesap istiyor ve ikincisi grafikte hiç görünmüyor — yalnızca eliyor.
# Tek ölçüyle bu soru yazılamıyordu.

_having_scope = ColumnScope()
_having_scope.add("t0", {"tutar": "Tutar", "ay": "Ay", "adet": "Adet"},
                  ["dbo.Siparisler", "Siparisler"], is_base=True)

_multi, _multi_params, _multi_note = analyzer._build_having(
    [{"op": ">", "value": 1000000},
     {"aggregation": "count", "op": "<", "value": 5}],
    measure, scope=_having_scope)

check("iki koşul iki yüklem üretir", len(_multi), 2)
check("ilk koşul sorgunun kendi ölçüsüne uygulanır",
      _multi[0].sql(), "SUM([t0].[Tutar]) > :h0")
check("ikinci koşul kendi işlemini kullanır",
      _multi[1].sql(), "COUNT(*) < :h1")
check("koşullar ayrı parametrelere bağlanır",
      _multi_params, {"h0": 1000000, "h1": 5})
# "ama" kelimesinin karşılığı VE; denetim izinde de öyle okunmalı.
check("denetim izinde koşullar VE ile birleşir",
      _multi_note, "SUM(Tutar) > 1000000 ve COUNT(*) < 5")

_other_col, _, _other_note = analyzer._build_having(
    {"column": "Adet", "aggregation": "sum", "op": ">=", "value": 10},
    measure, scope=_having_scope)
check("koşul başka bir kolona uygulanabilir",
      _other_col[0].sql(), "SUM([t0].[Adet]) >= :h0")
check("başka kolonun koşulu denetim izinde yazar", _other_note, "SUM(Adet) >= 10")

expect_error(
    "koşulda olmayan kolon reddedilir",
    lambda: analyzer._build_having(
        {"column": "BoyleBirKolonYok", "aggregation": "sum", "op": ">", "value": 1},
        measure, scope=_having_scope),
    "bulunamadı", ValueError,
)

expect_error(
    "koşulda tanınmayan işlem reddedilir",
    lambda: analyzer._build_having(
        {"aggregation": "median", "op": ">", "value": 1}, measure,
        scope=_having_scope),
    "koşulda kullanılamaz", ValueError,
)

windows, wnote, series = analyzer._build_window(
    "running_total", measure, [group_ref], "desc")
check("birikim çerçevesini kendisi kurar", windows[0].frame, "cumulative")
check("birikim seri sayılır", series, True)
check("birikim denetim izine yazılır", wnote.startswith("birikimli toplam"), True)

# "Aylara gore, ulke bazinda birikimli ciro" sorusunda dogru cevap ulke
# BASINA birikim; hepsini tek siraya dizmek ulkeleri birbirinin ustune toplar.
windows, _, _ = analyzer._build_window(
    "running_total", measure, [group_ref, ColumnRef("t0", "Ulke")], "desc")
check("ikinci kırılım bölüm olur", windows[0].partition_by, [ColumnRef("t0", "Ulke")])
check("eksen yine ilk kırılım", windows[0].order_by[0].target, group_ref)

windows, _, series = analyzer._build_window(
    {"function": "moving_average", "periods": 3}, measure, [group_ref], "desc")
# 3 donem = bu satir + 2 onceki.
check("hareketli ortalama pencereyi doğru kurar", windows[0].preceding, 2)
check("hareketli ortalama seri sayılır", series, True)

windows, _, series = analyzer._build_window(
    {"function": "rank"}, measure, [group_ref], "desc")
check("sıralama ölçüye göre yapılır", windows[0].order_by[0].target, measure)
# Siralama serinin sirasini bozmuyor: sonuc yine olcuye gore okunur.
check("sıralama seri değildir", series, False)

expect_error(
    "tek dönemlik hareketli ortalama olmaz",
    lambda: analyzer._build_window(
        {"function": "moving_average", "periods": 1}, measure, [group_ref], "desc"),
    "2 ya da daha büyük olmalı", ValueError,
)

expect_error(
    "kırılımsız birikim olmaz",
    lambda: analyzer._build_window("running_total", measure, [], "desc"),
    "kırılım gerekiyor", ValueError,
)

expect_error(
    "bilinmeyen pencere kalıbı reddedilir",
    lambda: analyzer._build_window("kumulatif", measure, [group_ref], "desc"),
    "Bilinmeyen pencere hesabı", ValueError,
)


# ── Onay bekleyen eşleşmeler ──────────────────────────────────────────────
#
# Adları bir harf farklı olduğu için tahminle kurulan bağlantı, sonuçla
# birlikte kullanıcıya sorulacak. Denetim izine yazılmazsa soru hiç
# sorulmaz ve sistem tahminini sessizce doğru sayar.

_pending_analyzer = AgentAnalyzer(_StubDataPort())

_typo_edge = {
    "fromTable": "dbo.C_INT_Calc", "fromColumns": ["ReferanceId"],
    "toTable": "dbo.L_INT_ExportReference", "toColumns": ["ReferenceId"],
    "valueOverlap": 0.94, "source": "inferred", "needsConfirmation": True,
}

_pending_analyzer._record_pending(_typo_edge)
# Aynı kenar zincirde iki kez geçebilir; kullanıcıya iki kez sorulmamalı.
_pending_analyzer._record_pending(_typo_edge)

check(
    "onay bekleyen eşleşme denetim izine bir kez yazılır",
    _pending_analyzer.audit.get("pendingConfirmations"),
    [{
        "fromTable": "dbo.C_INT_Calc", "fromColumn": "ReferanceId",
        "fromColumns": ["ReferanceId"],
        "toTable": "dbo.L_INT_ExportReference", "toColumn": "ReferenceId",
        "toColumns": ["ReferenceId"],
        "valueOverlap": 0.94,
    }],
)

_settled = AgentAnalyzer(_StubDataPort())
_settled._record_pending({
    "fromTable": "dbo.A", "fromColumns": ["BId"],
    "toTable": "dbo.B", "toColumns": ["Id"],
    "source": "fk",
})
check(
    "hakkında karar verilmiş kenar sorulmaz",
    _settled.audit.get("pendingConfirmations"),
    None,
)

# Bir cevabın ne kadar sağlam durduğunu kullandığı EN ZAYIF bağlantı
# belirliyor. Sekiz tabloyu birleştiren bir sorguda yedisi bildirilmiş yabancı
# anahtar, biri kullanıcının beyanı olabilir — o cevap "doğrulanmış" değildir.

_evidence = AgentAnalyzer(_StubDataPort())
_evidence._record_evidence({"source": "fk"})
check("tek kaynak varsa o yazılır", _evidence.audit.get("evidence"), "fk")

_evidence._record_evidence({"source": "declared"})
check("daha zayıf kaynak öne geçer", _evidence.audit.get("evidence"), "declared")

_evidence._record_evidence({"source": "fk"})
check("güçlü kaynak zayıfı geri almaz", _evidence.audit.get("evidence"), "declared")

_unknown = AgentAnalyzer(_StubDataPort())
_unknown._record_evidence({"source": "bilinmeyen"})
check("tanınmayan kaynak çıkarım sayılır",
      _unknown.audit.get("evidence"), "inferred")

# Kullanıcının öğrettiği kod anlamına dayanıldıysa denetim izinde yazıyor.
# İki şart birden aranıyor — kolon adı VE değer — çünkü yanlış bir "sizin
# öğrettiğiniz bilgi kullanıldı" notu, hiç not olmamasından kötüdür.

_codes_scope = ColumnScope()
_codes_scope.add("t0", {"referencetype": "ReferenceType", "tutar": "Tutar"},
                 ["dbo.Kayit", "Kayit"], is_base=True)
_codes_config = {"codeValues": [
    {"table": "dbo.Kayit", "column": "ReferenceType",
     "values": ["ROD", "SEA"], "meanings": {"ROD": "karayolu"}},
]}

_codes = AgentAnalyzer(_StubDataPort())
_codes._record_learned_codes(_codes_config, {"ReferenceType": "ROD"}, _codes_scope)
check("öğretilmiş kod anlamı denetim izine yazılır",
      _codes.audit.get("learnedCodes"), ["ReferenceType = 'ROD' → karayolu"])

_unlearned = AgentAnalyzer(_StubDataPort())
_unlearned._record_learned_codes(_codes_config, {"ReferenceType": "SEA"}, _codes_scope)
check("anlamı öğretilmemiş kod için not yazılmaz",
      _unlearned.audit.get("learnedCodes"), None)

_wrong_column = AgentAnalyzer(_StubDataPort())
_wrong_column._record_learned_codes(_codes_config, {"Tutar": "ROD"}, _codes_scope)
check("değer tutsa da kolon tutmuyorsa not yazılmaz",
      _wrong_column.audit.get("learnedCodes"), None)

check("bildirilmiş kaynak etiketi",
      AgentAnalyzer._edge_source_label({"source": "fk"}), "doğrulanmış")
check("beyan kaynak etiketi",
      AgentAnalyzer._edge_source_label({"source": "declared"}), "sizin kurduğunuz")
check("çıkarım kaynak etiketi",
      AgentAnalyzer._edge_source_label({"source": "inferred"}), "çıkarsanmış")


# ── Birleşim + join ───────────────────────────────────────────────────────
#
# Birleşimin en doğal sorusu ("ithalat ve ihracatı müşteri ADINA göre kır")
# adı başka tablodan almayı gerektiriyor. Ad olmadan kırılım müşteri koduna
# düşer — kullanıcının okuyamayacağı bir grafik.
#
# Buradaki risk şu: dalların çıktı kolonları hizalanmazsa sorgu PATLAMAZ,
# seriler sessizce karışır. O yüzden üretilen SQL'in kendisi ölçülüyor.


class _UnionSqlCaptured(Exception):
    """Üretilen SQL yakalandı; testin veriye ihtiyacı yok."""

    def __init__(self, sql, params):
        self.sql = sql
        self.params = params


class _UnionPort(_SchemaPort):
    TABLES = {
        "Ithalat": ["Id", "MusteriId", "Tutar"],
        "Ihracat": ["Id", "MusteriId", "Tutar"],
        "Musteriler": ["Id", "Ad"],
    }

    def read_sql(self, sql, params=None, max_rows=None):
        p = params or {}
        # Şema sorgusu tablo adıyla geliyor; birleşim sorgusu gelmiyor.
        if "table" in p and "schema" in p:
            return _FakeFrame(self.TABLES[p["table"]])
        raise _UnionSqlCaptured(sql, p)


_union_edges = [
    edge("dbo.Ithalat", ["MusteriId"], "dbo.Musteriler", ["Id"]),
    edge("dbo.Ihracat", ["MusteriId"], "dbo.Musteriler", ["Id"]),
]

_union_analyzer = AgentAnalyzer(_UnionPort())


def run_union(**overrides):
    """Birleşimi çalıştırır ve üretilen SQL ile parametreleri döner."""
    _union_analyzer._selection = selected_schema(_UnionPort.TABLES)
    call = {
        "config": {"relationships": _union_edges},
        "union": {
            "label": "İthalat",
            "with": [{"table": "dbo.Ihracat", "label": "İhracat",
                      "joins": [{"as": "m", "from": "base", "table": "dbo.Musteriler"}]}],
        },
        "base_table": "dbo.Ithalat",
        "group_by": ["dbo.Musteriler.Ad"],
        "target_col": "Tutar",
        "aggregation": "sum",
        "filters": {},
        "joins": [{"as": "m", "from": "base", "table": "dbo.Musteriler"}],
        "having": None,
        "sort_order": "desc",
        "limit": 5,
        "chart_type": "bar",
        "title": "",
        "desc": "",
    }
    call.update(overrides)
    try:
        _union_analyzer._sql_union(
            call["config"], call["union"], call["base_table"], call["group_by"],
            call["target_col"], call["aggregation"], call["filters"],
            call["joins"], call["having"], call["sort_order"], call["limit"],
            call["chart_type"], call["title"], call["desc"])
    except _UnionSqlCaptured as captured:
        return captured.sql, captured.params
    raise AssertionError("birleşim SQL üretmeden döndü")


_union_sql, _union_params = run_union()

check("birleşimin iki dalı da kendi join'ini kuruyor",
      _union_sql.count("LEFT JOIN"), 2)
check("birleşim UNION ALL ile kuruluyor",
      "UNION ALL" in _union_sql, True)
# Kırılım bağlanan tablodan geliyor: ad, kod değil.
check("kırılım bağlanan tablonun kolonundan çözülüyor",
      _union_sql.count("[Ad]") >= 2, True)

# Pencere hesabı hâlâ reddediliyor ve bu bilinçli: birikim dal başına
# hesaplanırsa iki ayrı birikim çıkar ve grafik "kümülatif" dendiği halde
# kümülatif olmayan bir şey gösterir. Sessizce yanlış bir seri üretmektense
# açıkça reddetmek doğru.
_window_result = _union_analyzer.run_analysis(
    {"relationships": _union_edges,
     "tables": [{"name": "dbo.Ithalat"}, {"name": "dbo.Ihracat"}],
     "columns": [{"table": table, "column": "Tutar"} for table in ["dbo.Ithalat", "dbo.Ihracat"]]},
    {"analysis_type": "aggregation", "target_table": "dbo.Ithalat",
     "group_by": ["Tutar"], "target_column": "Tutar", "aggregation": "sum",
     "union": {"with": [{"table": "dbo.Ihracat"}]},
     "window": {"function": "running_total"}})

check("birleşimde pencere hesabı reddediliyor",
      _window_result.get("success"), False)
check("reddin sebebi yazıyor",
      "birleşimin tamamı" in (_window_result.get("error") or ""), True)

# Eşik her dala ayrı uygulanıyor: "toplamı 1M'yi geçen müşteriler" sorusu,
# ithalatta geçen ile ihracatta geçeni ayrı ayrı sorar. Birleşimden sonra
# uygulamak iki kaynağın toplamını eşiğe sokmak olurdu — sorulan bu değil.
_having_sql, _having_params = run_union(having={"op": ">", "value": 1000000})
check("eşik her dalda ayrı ayrı uygulanıyor",
      _having_sql.count("HAVING"), 2)
check("iki dalın eşik parametresi ayrı adlarda",
      sorted(k for k in _having_params if k.startswith("hu")),
      ["hu0_0", "hu1_0"])

# Dallar AYNI parametre sözlüğünü paylaşıyor. İki dalda da sıfırıncı adımda
# filtre varsa aynı ad iki farklı değere bağlanırdı ve biri sessizce
# ötekinin değerini alırdı.
_filtered_sql, _filtered_params = run_union(
    joins=[{"as": "m", "from": "base", "table": "dbo.Musteriler",
            "filter": {"Ad": "A"}}],
    union={
        "label": "İthalat",
        "with": [{"table": "dbo.Ihracat", "label": "İhracat",
                  "joins": [{"as": "m", "from": "base", "table": "dbo.Musteriler",
                             "filter": {"Ad": "B"}}]}],
    },
)
_join_filter_keys = sorted(k for k in _filtered_params if k.startswith("j"))
check("dal başına join filtresi ayrı parametre adı alıyor",
      len(_join_filter_keys), 2)
check("iki dalın join filtresi farklı değerlere bağlı",
      sorted(_filtered_params[k] for k in _join_filter_keys), ["A", "B"])


print("\n\n".join(FAILS) if FAILS else "TÜM TESTLER GEÇTİ")
if __name__ == "__main__":
    sys.exit(1 if FAILS else 0)
assert not FAILS, "\n".join(FAILS)
