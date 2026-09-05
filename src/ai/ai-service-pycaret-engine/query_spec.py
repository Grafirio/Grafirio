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
  6. HAVING supports global aggregates without GROUP BY: the implicit group
      produces one aggregate row or no row, never ungrouped source columns.
  7. Sirali bir birikim penceresinde cerceve acikca yazilmali. SQL'in ortuk
     varsayilani esit siralama degerlerini tek satirda toplar ve grafikte bu
     fark edilmez.
  8. Birlesimin dallari ayni cikti kolonlarini ayni adlarla uretmeli ve ayni
     parametre adini iki kez kullanmamali. Ikisi de sessizce yanlis grafik
     uretir — biri kolonlari kaydirarak, digeri bir dalin filtresini otekinin
     degeriyle calistirarak.
"""

from dataclasses import dataclass, field, replace
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
    #: SELECT listesindeki cikti adi. Neredeyse her zaman None: kolon kendi
    #: adiyla doner. Birlesimde (UNION) sart oluyor — iki tablo ayni seyi
    #: farkli adlarla tutabilir ("UlkeAdi" / "Country") ve birlesimin cikti
    #: adlari butun dallarda ayni olmak zorunda.
    label: Optional[str] = None

    def sql(self) -> str:
        return f"{quote_identifier(self.alias)}.{quote_identifier(self.name)}"

    @property
    def output_name(self) -> str:
        """Sonuc kumesinde bu kolonun gorunecegi ad."""
        return self.label or self.name

    def projection_sql(self) -> str:
        """
        SELECT listesindeki hâli. Etiket yoksa `sql()` ile aynidir — yani
        etiket kullanilmayan her sorguda uretilen metin degismiyor.
        """
        if self.label is None or self.label == self.name:
            return self.sql()
        return f"{self.sql()} AS {quote_identifier(self.label)}"


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

    def expression(self) -> str:
        """
        Toplulastirmanin kendisi, cikti adi olmadan: `SUM([t0].[Tutar])`.

        Iki yerde ayri ayri gerekiyor. HAVING'de: SQL Server cikti adina atif
        yapilmasina izin vermez, ifade tekrar yazilmak zorunda. Pencere
        fonksiyonunda: `SUM(SUM(...)) OVER (...)` — ic toplulastirma grup
        degerini, dis pencere onlarin uzerindeki birikimi verir.
        """
        func = self._FUNCS.get(self.func)
        if func is None:
            raise QuerySpecError(f"Bilinmeyen toplulaştırma: '{self.func}'")
        if self.column is None and func != "COUNT":
            raise QuerySpecError(f"'{self.func}' için bir kolon gerekiyor.")
        inner = self.column.sql() if self.column is not None else "*"
        return f"{func}({inner})"

    def sql(self) -> str:
        return f"{self.expression()} AS {quote_identifier(self.label)}"


BINARY_OPS = (">=", ">", "<=", "<", "=", "<>")


def _comparison_sql(left: str, op: str, param_keys: List[str]) -> str:
    """
    Karsilastirmanin metni. Sol taraf bir kolon da olabilir, toplulastirma
    ifadesi de — kural ikisinde de ayni, o yuzden tek yerde duruyor.
    """
    if op == "IS NULL":
        return f"{left} IS NULL"
    if op == "IS NOT NULL":
        return f"{left} IS NOT NULL"
    if op == "IN":
        if not param_keys:
            raise QuerySpecError("IN filtresi boş olamaz.")
        placeholders = ", ".join(f":{k}" for k in param_keys)
        return f"{left} IN ({placeholders})"
    if op in BINARY_OPS:
        if len(param_keys) != 1:
            raise QuerySpecError(f"'{op}' tam olarak bir değer bekler.")
        return f"{left} {op} :{param_keys[0]}"
    raise QuerySpecError(f"Bilinmeyen karşılaştırma: '{op}'")


@dataclass(frozen=True)
class Predicate:
    column: ColumnRef
    op: str
    # Bagli parametre adlari. Deger BURADA TUTULMAZ: metne hicbir sekilde
    # deger girmesin diye. Degerler cagiranin params sozlugunde durur.
    param_keys: List[str] = field(default_factory=list)

    _BINARY = BINARY_OPS

    def sql(self) -> str:
        return _comparison_sql(self.column.sql(), self.op, self.param_keys)


@dataclass(frozen=True)
class HavingPredicate:
    """
    Toplulastirma SONUCU uzerinde kosul: "toplami 1 milyonu gecen musteriler".

    WHERE'den farki sozdizimsel bir ayrinti degil, baska bir soru. WHERE
    satirlari toplama ONCESINDE eler; "tutari 1 milyondan buyuk satirlari
    topla" demektir. HAVING gruplar toplandiktan SONRA eler; "toplami 1
    milyonu gecen gruplari getir" demektir. Ikisi neredeyse hicbir veride
    ayni sonucu vermez ve karistirildiginda sorgu calisir, yalnizca cevap
    baska bir sorunun cevabi olur.

    Deger yine metne girmiyor: `param_keys` uzerinden bagli parametre.
    """

    aggregate: Aggregate
    op: str
    param_keys: List[str] = field(default_factory=list)

    def sql(self) -> str:
        return _comparison_sql(self.aggregate.expression(), self.op, self.param_keys)


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
    # Uc bicim:
    #   ColumnRef — kolonun kendisi.
    #   str       — toplulastirmanin cikti adi (label). Yalnizca sorgunun
    #               kendi ORDER BY'inda gecerli.
    #   Aggregate — toplulastirma ifadesinin tekrari. Pencerenin OVER
    #               icindeki siralamasi icin sart: takma adlar ayni SELECT
    #               icinde birbirini gormez, `ORDER BY [value]` orada
    #               "boyle bir kolon yok" hatasi verir.
    target: Union[ColumnRef, str, Aggregate]
    direction: str = "desc"

    def sql(self) -> str:
        if isinstance(self.target, ColumnRef):
            expr = self.target.sql()
        elif isinstance(self.target, Aggregate):
            expr = self.target.expression()
        else:
            expr = quote_identifier(self.target)
        return f"{expr} {'ASC' if str(self.direction).lower() == 'asc' else 'DESC'}"


# Pencere cerceveleri — "hangi satirlar hesaba katiliyor".
#
# ALL        : cerceve yok, bolumun tamami. Genel toplam icin.
# CUMULATIVE : bastan bu satira kadar. Birikimli toplam icin.
# MOVING     : son N satir. Hareketli ortalama icin.
FRAME_ALL = "all"
FRAME_CUMULATIVE = "cumulative"
FRAME_MOVING = "moving"


@dataclass(frozen=True)
class WindowExpr:
    """
    Pencere fonksiyonu — grup basina degil, gruplarin UZERINDE hesaplanan kolon.

    Iki isi var ve ikisi de toplulastirmayla yapilamiyor:

      * Siralama (`row_number`, `rank`, `dense_rank`): "kacinci sirada".
      * Birikim (`sum`, `avg`, `min`, `max`): "bugune kadarki toplam",
        "son 3 ayin ortalamasi", "genel toplam".

    Gruplu sorguda pencere ic ice yaziliyor: `SUM(SUM([t0].[Tutar])) OVER (...)`.
    Ic toplulastirma ayin toplamini, dis pencere aylarin uzerindeki birikimi
    verir. Bunu tek gecişte yapmanin baska yolu yok — pandas'a cekip orada
    hesaplamak, tabloyu bastan cekmek demek.
    """

    func: str
    label: str
    #: Birikim fonksiyonlarinda uzerinde calisilan toplulastirma. Siralama
    #: fonksiyonlari argumansizdir ve burasi None kalir.
    over: Optional[Aggregate] = None
    partition_by: List[ColumnRef] = field(default_factory=list)
    order_by: List[OrderBy] = field(default_factory=list)
    frame: str = FRAME_ALL
    #: Yalnizca MOVING cercevesinde: kac satir geriye bakilacagi.
    preceding: Optional[int] = None

    _RANKING = {"row_number": "ROW_NUMBER", "rank": "RANK", "dense_rank": "DENSE_RANK"}
    _AGG = {"sum": "SUM", "avg": "AVG", "min": "MIN", "max": "MAX", "count": "COUNT"}

    def is_ranking(self) -> bool:
        return str(self.func).lower() in self._RANKING

    def sql(self) -> str:
        key = str(self.func).lower()

        if key in self._RANKING:
            if self.over is not None:
                raise QuerySpecError(
                    f"'{self.func}' sıralama fonksiyonudur, üzerinde "
                    "toplulaştırma çalıştırmaz.")
            if not self.order_by:
                raise QuerySpecError(
                    f"'{self.func}' sıralama olmadan anlamsız: neye göre "
                    "sıralandığı belirtilmemişse sıra numarası da keyfi olur.")
            head = f"{self._RANKING[key]}()"
        elif key in self._AGG:
            if self.over is None:
                raise QuerySpecError(
                    f"'{self.func}' penceresi hangi ölçünün üzerinde "
                    "çalışacağını bilmiyor.")
            head = f"{self._AGG[key]}({self.over.expression()})"
        else:
            raise QuerySpecError(f"Bilinmeyen pencere fonksiyonu: '{self.func}'")

        parts = []
        if self.partition_by:
            parts.append("PARTITION BY " + ", ".join(c.sql() for c in self.partition_by))
        if self.order_by:
            parts.append("ORDER BY " + ", ".join(o.sql() for o in self.order_by))

        frame = self._frame_sql()
        if frame:
            parts.append(frame)

        return f"{head} OVER ({' '.join(parts)}) AS {quote_identifier(self.label)}"

    def _frame_sql(self) -> str:
        key = str(self.frame).lower()

        if key not in (FRAME_ALL, FRAME_CUMULATIVE, FRAME_MOVING):
            raise QuerySpecError(f"Bilinmeyen pencere çerçevesi: '{self.frame}'")

        if self.is_ranking():
            if key != FRAME_ALL:
                raise QuerySpecError("Sıralama fonksiyonuna çerçeve verilemez.")
            return ""

        if key == FRAME_ALL:
            # 3. Degismez: ortuk cerceve yok.
            #
            # ORDER BY'li bir pencereye cerceve verilmezse SQL varsayilani
            # RANGE UNBOUNDED PRECEDING olur — birikimli toplam gibi gorunur
            # ama esit siralama degerlerini tek satirda toplar. "Ocak ve
            # Subat ayni tutarda" oldugunda ikisi de toplamin tamamini
            # gosterir ve bu grafikte fark edilmez. Niyet acikca yazilmali.
            if self.order_by:
                raise QuerySpecError(
                    "Sıralı bir birikim penceresinde çerçeve açıkça "
                    "belirtilmeli: 'cumulative' (baştan bu satıra kadar) ya da "
                    "'moving' (son N satır). Belirtilmezse eşit sıralama "
                    "değerleri tek satırda toplanır ve sonuç sessizce şişer.")
            return ""

        if not self.order_by:
            raise QuerySpecError(
                "Çerçeveli pencere sıralama gerektirir; sırasız bir birikimde "
                "hangi satırların toplandığı belirsizdir.")

        if key == FRAME_CUMULATIVE:
            return "ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW"

        rows = self.preceding
        if not isinstance(rows, int) or isinstance(rows, bool) or rows < 1:
            raise QuerySpecError(
                "Hareketli pencerede kaç satır geriye bakılacağı belirtilmeli.")
        return f"ROWS BETWEEN {int(rows)} PRECEDING AND CURRENT ROW"

    def referenced_columns(self) -> List[ColumnRef]:
        """PARTITION BY, ORDER BY ve toplulastirmada gecen kolonlar."""
        columns: List[ColumnRef] = list(self.partition_by)
        for item in self.order_by:
            if isinstance(item.target, ColumnRef):
                columns.append(item.target)
            elif isinstance(item.target, Aggregate) and item.target.column is not None:
                columns.append(item.target.column)
        if self.over is not None and self.over.column is not None:
            columns.append(self.over.column)
        return columns


@dataclass
class QuerySpec:
    base: TableRef
    joins: List[Join] = field(default_factory=list)
    group_by: List[ColumnRef] = field(default_factory=list)
    aggregate: Optional[Aggregate] = None
    where: List[Predicate] = field(default_factory=list)
    #: Toplulastirma sonrasi kosullar. WHERE'in yerine gecmez: bkz.
    #: `HavingPredicate`.
    having: List[HavingPredicate] = field(default_factory=list)
    #: Gruplarin uzerinde hesaplanan ek kolonlar (sira numarasi, birikimli
    #: toplam, hareketli ortalama).
    windows: List[WindowExpr] = field(default_factory=list)
    order_by: List[OrderBy] = field(default_factory=list)
    limit: Optional[int] = None

    # Satir bazli okuma: taban tablonun butun kolonlari (`t0.*`).
    select_all: bool = False

    # TOP'un siralamasiz kullanildigi tek mesru durum: temsili ornek okumak.
    # Bilerek isaretlenmeli — kaza eseri belirsiz siralama olmasin diye.
    unordered_sample: bool = False

    def aliases(self) -> set:
        return {self.base.alias} | {j.table.alias for j in self.joins}

    def output_names(self) -> List[str]:
        """
        Sonuc kumesindeki kolon adlari, SELECT sirasiyla.

        Birlesim bunu dallar arasinda karsilastiriyor; grafigi cizen taraf da
        sonucu bu adlarla okuyor. `select_all` durumunda bilinemez — kolonlar
        tablonun kendisinden gelir — ve bos liste doner.
        """
        if self.select_all or self.aggregate is None:
            return []
        names = [c.output_name for c in self.group_by]
        names.append(self.aggregate.label)
        names.extend(w.label for w in self.windows)
        return names


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


def _validate_having(spec: QuerySpec, check, visible: set) -> None:
    """Validate aggregate predicates, including the implicit global group."""
    if not spec.having:
        return

    if spec.aggregate is None:
        raise QuerySpecError(
            "Toplulaştırması olmayan sorguda HAVING kullanılamaz; "
            "koşul satır bazlıysa WHERE'e girmesi gerekir.")

    for predicate in spec.having:
        if not isinstance(predicate, HavingPredicate):
            raise QuerySpecError(
                "HAVING koşulu bir toplulaştırmaya uygulanmalı; "
                "ham kolon koşulları WHERE'e girmeli.")
        if predicate.aggregate.column is not None:
            check(predicate.aggregate.column, "HAVING", visible)


def _validate_windows(spec: QuerySpec, check, visible: set) -> None:
    """
    Pencere fonksiyonlarinin denetimi.

    Ikisi ayri dert:

      * Kapsam. Gruplu bir sorguda pencerenin ORDER BY / PARTITION BY'i
        yalnizca KIRILIM kolonlarini ya da olcunun cikti adini gorebilir.
        Baska bir kolon SQL Server'da hata verir; mesaji ("... is invalid in
        the select list ...") kullanicinin anlayabilecegi bir sey degil.
      * Ad cakismasi. Iki pencere ayni etiketi ya da olcunun etiketini
        alirsa sonuc kumesinde ayni adli iki kolon olusur; pandas bunlari
        tek isim altinda gorur ve grafige yanlis seri girer.
    """
    if not spec.windows:
        return

    if spec.aggregate is None or spec.select_all:
        raise QuerySpecError(
            "Pencere fonksiyonu yalnızca toplulaştırmalı sorguda "
            "kullanılabilir; satır bazlı okumada hesaplanacak bir grup yok.")

    grouped = {c.alias + "." + c.name for c in spec.group_by}
    labels = {spec.aggregate.label} | {c.output_name for c in spec.group_by}

    for window in spec.windows:
        if window.label in labels:
            raise QuerySpecError(
                f"'{window.label}' adı sonuçta zaten var. Pencere kolonunun "
                "kendi adı olmalı; aynı adlı iki kolon grafiğe yanlış seri sokar.")
        labels.add(window.label)

        for column in window.referenced_columns():
            check(column, f"'{window.label}' penceresi", visible)

        for column in list(window.partition_by) + [
                o.target for o in window.order_by if isinstance(o.target, ColumnRef)]:
            if column.alias + "." + column.name not in grouped:
                raise QuerySpecError(
                    f"'{window.label}' penceresi '{column.name}' kolonuna "
                    "bakıyor ama o kolon kırılımda yok. Gruplu bir sorguda "
                    "pencere yalnızca kırılım kolonlarını görebilir.")

        for item in window.order_by:
            if isinstance(item.target, str):
                # Takma adlar ayni SELECT icinde birbirini gormez: OVER
                # icindeki `ORDER BY [value]` SQL Server'da "Invalid column
                # name" verir. Olcuye gore siralanacaksa ifadenin kendisi
                # tekrarlanmali (`OrderBy(Aggregate(...))`).
                raise QuerySpecError(
                    f"'{window.label}' penceresi '{item.target}' çıktı adına "
                    "göre sıralanamaz; pencere içinde takma ad görünmez. "
                    "Ölçünün kendisine göre sıralayın.")

        # Metin uretimindeki kurallar (cerceve, arguman) burada da patlasin:
        # dogrulama gecip render patlarsa hata, hatanin sebebinden cok sonra
        # ve baska bir yerde gorunur.
        window.sql()


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
    grouped = {(column.alias, column.name) for column in spec.group_by}
    for item in spec.order_by:
        if isinstance(item.target, ColumnRef):
            check(item.target, "ORDER BY", visible)
            if spec.aggregate is not None and (item.target.alias, item.target.name) not in grouped:
                raise QuerySpecError(
                    f"ORDER BY: '{item.target.name}' kolonu kırılımda yok; "
                    "toplulaştırmalı sorguda ham kolonla sıralama yapılamaz.")
        elif isinstance(item.target, Aggregate):
            if item.target.column is not None:
                check(item.target.column, "ORDER BY", visible)
        elif spec.aggregate is not None and item.target not in spec.output_names():
            raise QuerySpecError(
                f"ORDER BY: '{item.target}' sorgunun çıktı adları arasında yok.")
    if spec.aggregate is not None and spec.aggregate.column is not None:
        check(spec.aggregate.column, "toplulaştırma", visible)

    _validate_having(spec, check, visible)
    _validate_windows(spec, check, visible)

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

    if spec.select_all and (spec.aggregate is not None or spec.group_by):
        raise QuerySpecError("Toplulaştırmalı sorguda tüm kolonlar seçilemez.")

    if spec.aggregate is None and not spec.select_all:
        raise QuerySpecError("Sorgunun ne döndüreceği belirtilmemiş.")

    # 2. Degismez: TOP + benzersiz olmayan siralama = ayni soruya farkli cevap.
    is_global_aggregate = spec.aggregate is not None and not spec.group_by
    if spec.limit is not None and not spec.unordered_sample and not is_global_aggregate:
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


def _render_select(spec: QuerySpec, extra_projections: Optional[List[str]] = None) -> str:
    """
    Tek bir SELECT'in metni. `render` ve birlesim dallari buradan geciyor.

    Dogrulama yapmaz — cagiran zaten `validate` etmis olmali.
    """
    if spec.select_all:
        projection = f"{quote_identifier(spec.base.alias)}.*"
    else:
        parts = [c.projection_sql() for c in spec.group_by]
        parts.append(spec.aggregate.sql())  # type: ignore[union-attr]
        parts.extend(w.sql() for w in spec.windows)
        projection = ", ".join(parts)

    if extra_projections:
        projection = ", ".join([projection, *extra_projections])

    top = f"TOP ({int(spec.limit)}) " if spec.limit is not None else ""

    sql = [f"SELECT {top}{projection}", f"FROM {spec.base.sql()}"]
    sql.extend(join.sql() for join in spec.joins)

    if spec.where:
        sql.append("WHERE " + " AND ".join(p.sql() for p in spec.where))
    if spec.group_by:
        sql.append("GROUP BY " + ", ".join(c.sql() for c in spec.group_by))
    # HAVING gruplamadan SONRA, siralamadan ONCE: SQL'in sirasi da mantigin
    # sirasi — once gruplar olusur, sonra elenir, en son siralanir.
    if spec.having:
        sql.append("HAVING " + " AND ".join(h.sql() for h in spec.having))
    if spec.order_by:
        sql.append("ORDER BY " + ", ".join(o.sql() for o in spec.order_by))

    return " ".join(sql)


def render(spec: QuerySpec) -> str:
    """
    Agactan SQL metni. Metin ureten TEK yer burasi.

    Deger uretmez: butun degerler `Predicate.param_keys` uzerinden bagli
    parametre olarak gecer.
    """
    validate(spec)
    return _render_select(spec)


@dataclass(frozen=True)
class UnionBranch:
    """
    Birlesimin bir dali: kendi tablosu, kendi filtreleri olan bir sorgu ve o
    dalin nereden geldigini soyleyen sabit etiket.

    Etiket bir parametre adiyla veriliyor (`label_param`), degeriyle degil:
    "Ithalat"/"Ihracat" gibi metinler de SQL'e girmiyor.
    """

    spec: QuerySpec
    label_param: Optional[str] = None


@dataclass
class UnionSpec:
    """
    Iki ya da daha fazla sorgunun sonuclarinin alt alta eklenmesi.

    Ne zaman gerekiyor: ayni sey iki ayri tabloda tutuluyor ve kullanici
    ikisini bir arada gormek istiyor — "ithalat ve ihracatı aylara göre yan
    yana". Join bunu yapamaz; join tablolari YANYANA koyar, birlesim ALT
    ALTA. Ikisini karistirmak, iki farkli soruya ayni cevabi vermek olur.

    Degismezler:

      * Dallarin cikti kolonlari ADET ve AD olarak birebir ayni olmali.
        Farkli olursa veritabani ya hata verir ya da kolonlari sirayla
        eslestirir — ikincisi sessizce yanlis grafik demektir.
      * Parametre adlari dallar arasinda cakismamali. Ayni ad iki dalda
        farkli degere baglanamaz; biri sessizce digerinin degerini alirdi.
      * Dallarin kendi siralamasi ve TOP'u olmaz. Birlesimden onceki
        siralama anlamsizdir; SQL Server zaten reddeder.
    """

    branches: List[UnionBranch]
    #: Kaynak etiketi kolonunun cikti adi.
    label_column: str = "Kaynak"
    #: UNION ALL varsayilan: ayiklamayi kendiliginden yapan bir birlesim,
    #: iki dalda ayni cikan satirlari sessizce teke indirir.
    all_rows: bool = True
    #: Birlesimin tamami uzerindeki siralama. Yalnizca CIKTI ADINA gore
    #: yapilabilir: birlesimden sonra tablo takma adlari yok.
    order_by: List[OrderBy] = field(default_factory=list)
    limit: Optional[int] = None


def _spec_param_keys(spec: QuerySpec) -> List[str]:
    """Bir sorgunun metne koydugu butun bagli parametre adlari."""
    keys: List[str] = []
    for predicate in spec.where:
        keys.extend(predicate.param_keys)
    for predicate in spec.having:
        keys.extend(predicate.param_keys)
    for join in spec.joins:
        for condition in join.conditions:
            if isinstance(condition, Predicate):
                keys.extend(condition.param_keys)
        if isinstance(join.table, DerivedTable):
            for predicate in join.table.where:
                keys.extend(predicate.param_keys)
    return keys


def validate_union(union: UnionSpec) -> None:
    """Birlesimin degismezleri. Dallarin kendisi ayrica `validate`'ten gecer."""
    if len(union.branches) < 2:
        raise QuerySpecError(
            "Birleşim en az iki dal ister; tek dallı birleşim sorgunun kendisidir.")

    labelled = [b for b in union.branches if b.label_param]
    if labelled and len(labelled) != len(union.branches):
        raise QuerySpecError(
            "Kaynak etiketi ya bütün dallarda olmalı ya hiçbirinde; bir kısmı "
            "etiketliyse çıktı kolonları dallar arasında tutmaz.")

    expected: Optional[List[str]] = None
    seen_params: set = set()

    for index, branch in enumerate(union.branches):
        spec = branch.spec
        validate(spec)

        if spec.select_all:
            raise QuerySpecError(
                "Birleşimin dalı satır bazlı olamaz: hangi kolonların "
                "döneceği tablodan tabloya değişir ve dallar tutmaz.")
        if spec.limit is not None or spec.order_by:
            raise QuerySpecError(
                "Birleşimin dalında sıralama ya da TOP olamaz; ikisi de "
                "birleşimin tamamına uygulanır.")

        names = spec.output_names()
        if expected is None:
            expected = names
        elif names != expected:
            raise QuerySpecError(
                f"{index + 1}. dalın çıktı kolonları diğerlerinden farklı: "
                f"{', '.join(names)} ≠ {', '.join(expected)}. Birleşimde "
                "kolonlar sırayla eşleşir; farklı olursa sonuç sessizce "
                "karışır.")

        keys = set(_spec_param_keys(spec))
        if branch.label_param:
            keys.add(branch.label_param)
        clash = keys & seen_params
        if clash:
            raise QuerySpecError(
                f"'{', '.join(sorted(clash))}' parametre adı birden fazla "
                "dalda kullanılmış. Dallar aynı parametre sözlüğünü paylaşır; "
                "aynı ad iki farklı değere bağlanamaz.")
        seen_params |= keys

    if union.limit is not None and not union.order_by:
        raise QuerySpecError(
            "Sıralaması olmayan bir birleşimde TOP kullanılamaz; hangi dalın "
            "satırlarının kalacağı belirsiz olur.")

    for item in union.order_by:
        if not isinstance(item.target, str):
            raise QuerySpecError(
                "Birleşimin sıralaması yalnızca çıktı adına göre yapılabilir; "
                "birleşimden sonra tablo takma adları yok.")
        if expected is not None and item.target not in expected and item.target != union.label_column:
            raise QuerySpecError(
                f"Birleşim '{item.target}' adına göre sıralanıyor ama "
                "çıktıda böyle bir kolon yok.")


def render_union(union: UnionSpec) -> str:
    """
    Birlesimin SQL metni.

    Dallarin tamami bir turetilmis tabloya sariliyor. Sebebi SQL Server:
    TOP dogrudan birlesimin uzerine yazilamaz, sarmalanmadan ORDER BY da yalnizca
    son dala aitmis gibi ayristirilabilir. Tek bicim, iki ayri yol tutmaktan iyi.
    """
    validate_union(union)

    keyword = "UNION ALL" if union.all_rows else "UNION"
    parts = []
    for branch in union.branches:
        extra = ([f":{branch.label_param} AS {quote_identifier(union.label_column)}"]
                 if branch.label_param else None)
        parts.append(_render_select(branch.spec, extra))

    body = f" {keyword} ".join(parts)
    top = f"TOP ({int(union.limit)}) " if union.limit is not None else ""

    sql = [f"SELECT {top}* FROM ({body}) AS {quote_identifier('u')}"]
    if union.order_by:
        sql.append("ORDER BY " + ", ".join(o.sql() for o in union.order_by))

    return " ".join(sql)


def render_group_count(spec: QuerySpec) -> str:
    """
    Kirilimin toplam grup sayisi — "5 grup gosteriliyor (toplam 37)" icin.
    Ayni agaci kullanir ki filtreler ve join'ler birebir ayni olsun.
    """
    validate(spec)

    if not spec.group_by:
        if spec.aggregate is None:
            raise QuerySpecError("Toplulaştırması olmayan sorguda grup sayısı istenemez.")
        # HAVING can suppress the implicit group, including on empty input.
        scalar_spec = replace(spec, order_by=[], limit=None, windows=[])
        return "SELECT COUNT(*) FROM (" + _render_select(scalar_spec) + ") AS g"

    group_sql = ", ".join(c.sql() for c in spec.group_by)
    body = [f"SELECT {group_sql}", f"FROM {spec.base.sql()}"]
    body.extend(join.sql() for join in spec.joins)
    if spec.where:
        body.append("WHERE " + " AND ".join(p.sql() for p in spec.where))
    body.append("GROUP BY " + group_sql)
    # HAVING sayima da girmeli: elenmis gruplari saymak, "5 grup gosteriliyor
    # (toplam 37)" satirinin yalan soylemesi demek — kullanici goremedigi 32
    # grubun filtreye takildigini degil, sayfada olmadigini sanir.
    if spec.having:
        body.append("HAVING " + " AND ".join(h.sql() for h in spec.having))

    return "SELECT COUNT(*) FROM (" + " ".join(body) + ") AS g"
