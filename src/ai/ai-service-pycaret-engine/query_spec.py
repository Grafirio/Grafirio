"""
Sorgu ara temsili (IR) ve SQL uretici.

Neden ayri bir katman: SQL metni onceden analiz kodunun icinde f-string'lerle,
parca parca kuruluyordu. Tek tablo icin idare ediyordu; join eklendiginde bu
yaklasim kesin kirilir — takma ad nitelemesi, join tipi, siralama
belirleyiciligi gibi kurallarin hepsi metin birlestirme aninda unutulabilir
seyler.

Entity Framework'un yaptigini yapiyoruz: sorgu once TIPLI BIR AGAC olarak
kurulur (`QuerySpec` — EF'teki `SelectExpression`in karsiligi), dogrulama ve
kurallar bu agac uzerinde calisir, SQL metni en sonda TEK bir fonksiyonda
uretilir (`render` — EF'teki `QuerySqlGenerator`). Metin ureten baska hicbir
yer yok.

Bes degismez kural burada zorlanir:

  1. Toplulastirma varken ham 1:N join'e izin verilmez. Satirlari cogaltir ve
     COUNT(*) artik baska bir seyi sayar — hicbir uyari vermeden. Cogalmanin
     kasitli oldugu durumlar (fatura basina kalem toplami) `DerivedTable` ile
     karsilanir: cocuk tablo kendi anahtarina gore onceden toplanir, boylece
     dis sorguya N:1 baglanir ve cogalma hic olusmaz.
  2. TOP kullanan her sorgunun siralamasi benzersiz olmalidir. Aksi halde ayni
     soru iki farkli cevap dondurebilir.
  3. Butun kolon referanslari takma adla nitelenir; tanimlayicilar tek bir
     fonksiyondan gecer.
  4. Bir join yalnizca KENDINDEN ONCE acilmis takma adlara atif yapabilir.
     Zincirleme join'lerde ileri referans, dogrulamayi gecip gecersiz SQL
     uretirdi.
  5. Ayni tabloya birden fazla kez baglanmak serbest, ama her join'in kendi
     takma adi olmali ve onlari birbirinden ayiran sabit kosullar ON'a girmeli.
"""

from dataclasses import dataclass, field
from typing import List, Optional, Union


class QuerySpecError(ValueError):
    """Sorgu agaci gecersiz. Metin uretilmeden once fark edilen her sey."""


def quote_identifier(identifier: str) -> str:
    """
    Tanimlayicilarin sinirlandirildigi TEK yer.

    Buraya gelen ad her zaman veritabani semasindan okunmus gercek addir;
    kullanici ya da model metni hicbir zaman dogrudan gecmez.
    """
    return "[" + str(identifier).replace("]", "]]") + "]"


class AliasFactory:
    """t0, t1, t2 ... Takma ad cakismasi diye bir kavram kalmasin diye."""

    def __init__(self, prefix: str = "t"):
        self._prefix = prefix
        self._next = 0

    def take(self) -> str:
        alias = f"{self._prefix}{self._next}"
        self._next += 1
        return alias


@dataclass(frozen=True)
class TableRef:
    schema: str
    name: str
    alias: str

    def sql(self) -> str:
        return (f"{quote_identifier(self.schema)}.{quote_identifier(self.name)} "
                f"AS {quote_identifier(self.alias)}")

    def qualified(self) -> str:
        return f"{self.schema}.{self.name}"


@dataclass(frozen=True)
class ColumnRef:
    alias: str   # tablo takma adi
    name: str    # semadan okunmus gercek kolon adi

    def sql(self) -> str:
        return f"{quote_identifier(self.alias)}.{quote_identifier(self.name)}"


@dataclass(frozen=True)
class JoinCondition:
    left: ColumnRef
    right: ColumnRef

    def sql(self) -> str:
        return f"{self.left.sql()} = {self.right.sql()}"


# Kardinaliteler. Toplulastirmada yalnizca ilk ikisi guvenli: bir taban satiri
# en fazla bir karsi satirla eslesir, satir sayisi degismez.
MANY_TO_ONE = "many-to-one"
ONE_TO_ONE = "one-to-one"
ONE_TO_MANY = "one-to-many"

SAFE_FOR_AGGREGATION = (MANY_TO_ONE, ONE_TO_ONE)


@dataclass(frozen=True)
class Join:
    # Kaynak ya bir tablo ya da on toplanmis bir alt sorgu (`DerivedTable`).
    # Ikisi de ayni uc seyi sunuyor: `alias`, `sql()`, `qualified()`.
    table: Union["TableRef", "DerivedTable"]
    # Kolon esleme kosullari (`JoinCondition`) ve ayirt edici sabit kosullar
    # (`Predicate`) bir arada. Sabit kosul, ayni tabloya birden fazla kez
    # baglanirken sart: bir parametre tablosu hem para birimi hem odeme tipi
    # tutuyorsa ON'a `AND [t3].[Tip] = :p0` girmeden iki join birbirinden
    # ayrilamaz. Deger yine metne girmiyor — `Predicate` parametreye bagliyor.
    conditions: List[Union[JoinCondition, "Predicate"]]
    cardinality: str = MANY_TO_ONE
    # LEFT varsayilan ve neredeyse her zaman dogrusu: INNER, eslesmeyen taban
    # satirlarini sessizce dusurur ve sayiyi degistirir. INNER yalnizca
    # bildirilmis, guvenilir (is_not_trusted = 0) ve NOT NULL bir FK icin
    # secilmeli — cikarsanmis kenarda asla.
    kind: str = "left"

    def sql(self) -> str:
        keyword = "INNER JOIN" if self.kind == "inner" else "LEFT JOIN"
        on = " AND ".join(c.sql() for c in self.conditions)
        return f"{keyword} {self.table.sql()} ON {on}"

    def referenced_columns(self) -> List[ColumnRef]:
        """ON kosullarinda gecen butun kolon referanslari."""
        columns: List[ColumnRef] = []
        for condition in self.conditions:
            if isinstance(condition, JoinCondition):
                columns.append(condition.left)
                columns.append(condition.right)
            else:
                columns.append(condition.column)
        return columns


@dataclass(frozen=True)
class Aggregate:
    func: str                       # count | sum | avg | min | max
    column: Optional[ColumnRef]     # None => COUNT(*)
    label: str                      # cikti kolonunun adi

    _FUNCS = {"count": "COUNT", "sum": "SUM", "avg": "AVG", "min": "MIN", "max": "MAX"}

    def sql(self) -> str:
        func = self._FUNCS.get(self.func)
        if func is None:
            raise QuerySpecError(f"Bilinmeyen toplulaştırma: '{self.func}'")
        inner = self.column.sql() if self.column is not None else "*"
        if self.column is None and func != "COUNT":
            raise QuerySpecError(f"'{self.func}' için bir kolon gerekiyor.")
        return f"{func}({inner}) AS {quote_identifier(self.label)}"


@dataclass(frozen=True)
class Predicate:
    column: ColumnRef
    op: str
    # Bagli parametre adlari. Deger BURADA TUTULMAZ: metne hicbir sekilde
    # deger girmesin diye. Degerler cagiranin params sozlugunde durur.
    param_keys: List[str] = field(default_factory=list)

    _BINARY = (">=", ">", "<=", "<", "=", "<>")

    def sql(self) -> str:
        if self.op == "IS NULL":
            return f"{self.column.sql()} IS NULL"
        if self.op == "IS NOT NULL":
            return f"{self.column.sql()} IS NOT NULL"
        if self.op == "IN":
            if not self.param_keys:
                raise QuerySpecError("IN filtresi boş olamaz.")
            placeholders = ", ".join(f":{k}" for k in self.param_keys)
            return f"{self.column.sql()} IN ({placeholders})"
        if self.op in self._BINARY:
            if len(self.param_keys) != 1:
                raise QuerySpecError(f"'{self.op}' tam olarak bir değer bekler.")
            return f"{self.column.sql()} {self.op} :{self.param_keys[0]}"
        raise QuerySpecError(f"Bilinmeyen karşılaştırma: '{self.op}'")


@dataclass(frozen=True)
class DerivedTable:
    """
    On toplanmis alt sorgu. Join'de tablo yerine gecer.

    Neden var: bire-cok bir tabloyla dogrudan birlestirmek satirlari cogaltir.
    "Fatura basina kalem tutarlarini topla" mesru bir istektir, ama ham join'de
    faturaya ait her sey kalem sayisi kadar tekrarlanir ve COUNT(*) artik
    faturalari degil kalemleri sayar.

    Cozum, cogalmayi hic olusturmamak: cocuk tablo kendi anahtarina gore
    gruplanip toplanir, boylece anahtar basina TEK satir uretir ve dis sorguya
    tanim geregi N:1 baglanir. Bu sayede "fatura sayisi" ile "kalem toplami"
    ayni sorguda, ikisi de dogru cikar.

    Ic takma adlar disariya sizmaz: alt sorgunun disinda yalnizca bu nesnenin
    `alias`i ve `output_columns()` icindeki adlar gorunur.
    """

    source: TableRef
    #: GROUP BY ve ayni zamanda disariya acilan join anahtari.
    key_columns: List[ColumnRef]
    aggregate: Aggregate
    alias: str
    #: Cocuk tarafa ait filtreler. Toplama ONCE uygulanir — sonra uygulamak
    #: baska bir soruyu cevaplar.
    where: List[Predicate] = field(default_factory=list)

    def sql(self) -> str:
        keys = ", ".join(c.sql() for c in self.key_columns)
        body = [f"SELECT {keys}, {self.aggregate.sql()}", f"FROM {self.source.sql()}"]
        if self.where:
            body.append("WHERE " + " AND ".join(p.sql() for p in self.where))
        body.append(f"GROUP BY {keys}")
        return "(" + " ".join(body) + f") AS {quote_identifier(self.alias)}"

    def qualified(self) -> str:
        return self.source.qualified()

    def output_columns(self) -> set:
        """Disaridan gorulebilen kolon adlari."""
        return {c.name for c in self.key_columns} | {self.aggregate.label}


@dataclass(frozen=True)
class OrderBy:
    # Kolon ya da toplulastirmanin cikti adi (label).
    target: Union[ColumnRef, str]
    direction: str = "desc"

    def sql(self) -> str:
        expr = self.target.sql() if isinstance(self.target, ColumnRef) else quote_identifier(self.target)
        return f"{expr} {'ASC' if str(self.direction).lower() == 'asc' else 'DESC'}"


@dataclass
class QuerySpec:
    base: TableRef
    joins: List[Join] = field(default_factory=list)
    group_by: List[ColumnRef] = field(default_factory=list)
    aggregate: Optional[Aggregate] = None
    where: List[Predicate] = field(default_factory=list)
    order_by: List[OrderBy] = field(default_factory=list)
    limit: Optional[int] = None

    # Satir bazli okuma: taban tablonun butun kolonlari (`t0.*`).
    select_all: bool = False

    # TOP'un siralamasiz kullanildigi tek mesru durum: temsili ornek okumak.
    # Bilerek isaretlenmeli — kaza eseri belirsiz siralama olmasin diye.
    unordered_sample: bool = False

    def aliases(self) -> set:
        return {self.base.alias} | {j.table.alias for j in self.joins}


def _validate_derived(join: "Join") -> None:
    """
    On toplanmis join'in gercekten teklik garantisi tasidigini dogrular.

    Iki kosul birden saglanmali, yoksa "cogalma imkânsiz" iddiasi cokler:

      1. Alt sorgunun ic referanslari yalnizca kendi kaynak tablosunu
         gostermeli. Disaridan bir takma ada atif, kapsam disi bir baglantidir.
      2. Join, anahtarin TAMAMI uzerinden yapilmali. Iki kolonluk bir anahtarin
         yalnizca birinden baglanmak, anahtar basina tek satir garantisini
         gecersiz kilar ve satirlar yeniden cogalir.
    """
    derived = join.table
    inner = derived.source.alias

    if not derived.key_columns:
        raise QuerySpecError(
            f"'{derived.qualified()}' ön toplaması gruplama anahtarı olmadan "
            "yapılamaz; sonuç tek satıra iner ve bağlanacak bir şey kalmaz.")

    inner_refs = list(derived.key_columns) + [p.column for p in derived.where]
    if derived.aggregate.column is not None:
        inner_refs.append(derived.aggregate.column)

    for column in inner_refs:
        if column.alias != inner:
            raise QuerySpecError(
                f"'{derived.qualified()}' ön toplamasının içinde '{column.alias}' "
                "takma adı kullanılmış; alt sorgu yalnızca kendi tablosunu görür.")

    joined_on = {c.name for c in join.referenced_columns() if c.alias == derived.alias}
    missing = [c.name for c in derived.key_columns if c.name not in joined_on]
    if missing:
        raise QuerySpecError(
            f"'{derived.qualified()}' ön toplaması {', '.join(c.name for c in derived.key_columns)} "
            f"anahtarına göre yapılmış ama join {', '.join(missing)} kolonunu "
            "kullanmıyor. Anahtarın tamamı üzerinden bağlanmazsa satırlar yine çoğalır.")


def validate(spec: QuerySpec) -> None:
    """
    Agaci metin uretilmeden once denetler.

    EF Core 3.0'in cevrilemeyen sorguyu sessizce istemcide calistirmak yerine
    istisna firlatmaya gecmesiyle ayni gerekce: "calisiyor gibi gorunen yanlis",
    acik hatadan beterdir.
    """
    # Takma adlar SIRAYLA aciliyor. Onceden butun adlar tek kume olarak
    # toplaniyordu; o hâlde bir join kendinden SONRAKI bir takma ada atif
    # yapsa dogrulamadan gecer, uretilen SQL ise gecersiz olurdu. Tek tabloda
    # ve yildiz biciminde ortaya cikmiyor — zincirde cikiyor.
    visible = {spec.base.alias}

    #: Yalnizca turetilmis tablolar icin: disaridan gorulebilen kolon adlari.
    projected = {}

    def check(column: ColumnRef, where: str, scope: set) -> None:
        if column.alias not in scope:
            raise QuerySpecError(
                f"{where}: '{column.alias}' takma adı bu noktada tanımlı değil.")
        allowed = projected.get(column.alias)
        if allowed is not None and column.name not in allowed:
            raise QuerySpecError(
                f"{where}: '{column.name}', ön toplanmış '{column.alias}' "
                "sonucunda yok. Alt sorgunun döndürdüğü kolonlar: "
                f"{', '.join(sorted(allowed))}.")

    for join in spec.joins:
        if not join.conditions:
            raise QuerySpecError(
                f"'{join.table.qualified()}' join'inin eşleşme koşulu yok.")

        if join.table.alias in visible:
            raise QuerySpecError(
                f"'{join.table.alias}' takma adı iki kez kullanılmış. Aynı "
                "tabloya birden fazla kez bağlanmak serbest, ama her birinin "
                "kendi takma adı olmalı.")

        if isinstance(join.table, DerivedTable):
            _validate_derived(join)
            projected[join.table.alias] = join.table.output_columns()

        # ON kosulu kendi takma adini ve kendinden ONCE acilmis adlari gorur.
        on_scope = visible | {join.table.alias}
        for column in join.referenced_columns():
            check(column, "join koşulu", on_scope)

        visible.add(join.table.alias)

    for column in spec.group_by:
        check(column, "GROUP BY", visible)
    for predicate in spec.where:
        check(predicate.column, "WHERE", visible)
    for item in spec.order_by:
        if isinstance(item.target, ColumnRef):
            check(item.target, "ORDER BY", visible)
    if spec.aggregate is not None and spec.aggregate.column is not None:
        check(spec.aggregate.column, "toplulaştırma", visible)

    # 1. Degismez: toplulastirma + 1:N = sessizce yanlis sayi.
    #
    # Turetilmis tablo bu kuralin disinda, cunku cogalma matematiksel olarak
    # imkânsiz: alt sorgu kendi anahtarina gore gruplandigi icin anahtar
    # basina en fazla bir satir uretir. `_validate_derived` join'in gercekten
    # o anahtarin TAMAMI uzerinden yapildigini ayrica dogruluyor — eksik
    # anahtarla baglanirsa teklik garantisi kalmaz.
    if spec.aggregate is not None:
        for join in spec.joins:
            if isinstance(join.table, DerivedTable):
                continue
            if join.cardinality not in SAFE_FOR_AGGREGATION:
                raise QuerySpecError(
                    f"'{join.table.qualified()}' ilişkisi bire-çok. Bu tabloyla "
                    "doğrudan birleştirmek satırları çoğaltır ve sayılar yanlış "
                    "çıkar. Bu tablonun ön toplanmış hâliyle bağlanması gerekiyor.")

    if spec.select_all and spec.aggregate is not None:
        raise QuerySpecError("Toplulaştırmalı sorguda tüm kolonlar seçilemez.")

    if spec.aggregate is None and not spec.select_all:
        raise QuerySpecError("Sorgunun ne döndüreceği belirtilmemiş.")

    # 2. Degismez: TOP + benzersiz olmayan siralama = ayni soruya farkli cevap.
    if spec.limit is not None and not spec.unordered_sample:
        if not spec.order_by:
            raise QuerySpecError(
                "Sıralaması olmayan bir sorguda TOP kullanılamaz; sonuç "
                "belirsiz olur.")
        ordered = {c.alias + "." + c.name
                   for c in (i.target for i in spec.order_by) if isinstance(c, ColumnRef)}
        missing = [c for c in spec.group_by if c.alias + "." + c.name not in ordered]
        if missing:
            raise QuerySpecError(
                "TOP kullanan gruplu sorguda sıralama benzersiz olmalı; "
                f"eksik kırılım kolonu: {', '.join(c.name for c in missing)}")


def render(spec: QuerySpec) -> str:
    """
    Agactan SQL metni. Metin ureten TEK yer burasi.

    Deger uretmez: butun degerler `Predicate.param_keys` uzerinden bagli
    parametre olarak gecer.
    """
    validate(spec)

    if spec.select_all:
        projection = f"{quote_identifier(spec.base.alias)}.*"
    else:
        parts = [c.sql() for c in spec.group_by]
        parts.append(spec.aggregate.sql())  # type: ignore[union-attr]
        projection = ", ".join(parts)

    top = f"TOP ({int(spec.limit)}) " if spec.limit is not None else ""

    sql = [f"SELECT {top}{projection}", f"FROM {spec.base.sql()}"]
    sql.extend(join.sql() for join in spec.joins)

    if spec.where:
        sql.append("WHERE " + " AND ".join(p.sql() for p in spec.where))
    if spec.group_by:
        sql.append("GROUP BY " + ", ".join(c.sql() for c in spec.group_by))
    if spec.order_by:
        sql.append("ORDER BY " + ", ".join(o.sql() for o in spec.order_by))

    return " ".join(sql)


def render_group_count(spec: QuerySpec) -> str:
    """
    Kirilimin toplam grup sayisi — "5 grup gosteriliyor (toplam 37)" icin.
    Ayni agaci kullanir ki filtreler ve join'ler birebir ayni olsun.
    """
    validate(spec)

    if not spec.group_by:
        raise QuerySpecError("Gruplaması olmayan sorguda grup sayısı istenemez.")

    group_sql = ", ".join(c.sql() for c in spec.group_by)
    body = [f"SELECT {group_sql}", f"FROM {spec.base.sql()}"]
    body.extend(join.sql() for join in spec.joins)
    if spec.where:
        body.append("WHERE " + " AND ".join(p.sql() for p in spec.where))
    body.append("GROUP BY " + group_sql)

    return "SELECT COUNT(*) FROM (" + " ".join(body) + ") AS g"
