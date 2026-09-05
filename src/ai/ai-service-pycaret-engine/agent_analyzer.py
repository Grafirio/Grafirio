"""
PyCaret Agent Analyzer — LLM config'i ile dinamik analiz yapan modül.
MassTransit'ten gelen mesajları işler ve sonuçları geri gönderir.
"""

import json
import logging
import pandas as pd
from typing import Dict, Any, List, Optional

from data_port import DataPort
from analysis_scope import AnalysisScope, table_parts
from chart_validation import validate_charts, validate_chart_type
from query_spec import (
    AliasFactory, Aggregate, ColumnRef, DerivedTable, FRAME_CUMULATIVE,
    FRAME_MOVING, HavingPredicate, Join, JoinCondition, MANY_TO_ONE, OrderBy,
    Predicate, QuerySpec, QuerySpecError, TableRef, UnionBranch, UnionSpec,
    WindowExpr, render, render_group_count, render_union,
)

logger = logging.getLogger(__name__)


class ColumnScope:
    """
    Sorguda gorunen butun tablolarin kolonlari, takma adlariyla birlikte.

    Tek tabloda kolon adi tek basina yeterliydi. Iki tablo geldiginde ayni ad
    ikisinde birden bulunabiliyor ve hangisinin kastedildigi belirsizlesiyor.
    Belirsizligi tahminle kapatmak, sessizce yanlis tablodan okumak demek —
    o yuzden burada tahmin yok, ya cozulur ya hata verilir.

    Cozme sirasi:
      1. Nitelenmis ad ("dbo.Musteriler.Ad" ya da "musteri.Ad") dogrudan o
         tabloya gider.
      2. Ciplak ad once TABAN tabloda aranir. Boylece tek tablolu sorgular
         eskisi gibi calismaya devam eder.
      3. Tabanda yoksa diger tablolara bakilir. Tek eslesme varsa kabul,
         birden fazlaysa hata.
    """

    def __init__(self):
        # takma ad -> {kucuk harfli kolon adi: gercek ad}
        self._columns: Dict[str, Dict[str, str]] = {}
        # kullanicinin/modelin yazabilecegi ad -> takma ad
        self._names: Dict[str, str] = {}
        self._base_alias: Optional[str] = None

    def add(self, alias: str, columns: Dict[str, str], names: List[str],
            is_base: bool = False) -> None:
        self._columns[alias] = columns
        for name in names:
            if name:
                self._names[str(name).lower()] = alias
        if is_base:
            self._base_alias = alias

    def columns_of(self, alias: str) -> Dict[str, str]:
        return self._columns.get(alias, {})

    @property
    def base_alias(self) -> Optional[str]:
        return self._base_alias

    def resolve(self, raw: Optional[str]) -> Optional[ColumnRef]:
        """Nitelenmis ya da ciplak adi `ColumnRef`e cevirir. Bulamazsa None."""
        if not raw:
            return None

        cleaned = str(raw).replace("[", "").replace("]", "").strip()
        if not cleaned:
            return None

        parts = [p for p in cleaned.split(".") if p]
        if len(parts) > 1:
            column = parts[-1]
            # "dbo.Musteriler.Ad", "Musteriler.Ad" ve "musteri.Ad" — ucu de
            # gecerli; hangisiyle yazildigini bilemeyiz.
            for start in range(len(parts) - 1):
                qualifier = ".".join(parts[start:-1]).lower()
                alias = self._names.get(qualifier)
                if alias is not None:
                    real = self._columns.get(alias, {}).get(column.lower())
                    return ColumnRef(alias, real) if real else None
            return None

        key = parts[0].lower()

        if self._base_alias is not None:
            real = self._columns.get(self._base_alias, {}).get(key)
            if real is not None:
                return ColumnRef(self._base_alias, real)

        matches = [(alias, cols[key]) for alias, cols in self._columns.items()
                   if alias != self._base_alias and key in cols]

        if len(matches) == 1:
            return ColumnRef(matches[0][0], matches[0][1])
        if len(matches) > 1:
            raise ValueError(
                f"'{parts[0]}' kolonu birden fazla tabloda var. Hangisini "
                "kastettiğinizi tablo adıyla belirtin.")
        return None

    def known_columns(self) -> List[str]:
        """Hata mesajlarinda gosterilecek ornek kolonlar."""
        names: List[str] = []
        if self._base_alias:
            names.extend(self._columns.get(self._base_alias, {}).values())
        for alias, cols in self._columns.items():
            if alias != self._base_alias:
                names.extend(cols.values())
        return names


class AgentAnalyzer:
    """LLM tarafından oluşturulan config ve parametrelerle veri analizi yapar."""

    # Satir bazli okumada tavan. Yalnizca model egiten analizler (regression,
    # classification, anomaly, clustering) ve korelasyon/ozet istatistik
    # buraya girer; bunlar zaten ornek uzerinde calisir. Gruplama ve sayma
    # SQL'de yapildigi icin bu tavana takilmaz — sonuc tablonun tamamindan
    # cikar.
    MAX_ROWS = 50000

    def __init__(self, data: DataPort):
        # Baglanti degil, sorgu calistiran bir kapi. Veritabanina nasil
        # ulasildigi (dogrudan mi, musteri agindaki bridge uzerinden mi) burayi
        # ilgilendirmiyor.
        self.data = data
        # Denetim izi: uretilen sorgunun ve okunan satir sayisinin disari
        # verilebilmesi icin tutuluyor. Kullanicinin "hangi sorgu calisti,
        # dogru kolonu mu secti" sorusunu cevaplayabilmesi buna bagli.
        self.audit: Dict[str, Any] = {}
        self._selection = None

    def run_analysis(self, config: Dict, params: Dict) -> Dict[str, Any]:
        """
        Config ve parametrelere göre analiz yap, grafik verileri ve insights döndür.
        """
        self.audit = {}
        try:
            if not isinstance(config, dict) or not isinstance(params, dict):
                raise ValueError("Config and analysis parameters must be objects.")
            self._selection = AnalysisScope(config)
            validate_chart_type(params.get("chart_type", "bar"))
        except ValueError as error:
            return self._error_result(str(error))

        # Varsayilan `aggregation`: ceviri prompt'unun da varsayilani bu.
        # Onceden burada "statistics" yaziyordu, yani tipi bos gelen her soru
        # sessizce kolon ortalamalarina dusuyordu.
        analysis_type = str(params.get("analysis_type") or "aggregation").strip().lower()
        if analysis_type not in self.ANALYSIS_TYPES:
            # Taninmayan tip sorunun kendisini gecersiz kilmaz; varsayilana
            # dusmek sessiz bir yanlis degil, denetim izine yaziliyor.
            analysis_type = "aggregation"
        target_table = params.get("target_table")
        target_column = params.get("target_column")
        feature_columns = params.get("feature_columns", [])
        group_by = params.get("group_by") or []
        aggregation = params.get("aggregation") or "count"
        sort_by = params.get("sort_by")
        sort_order = params.get("sort_order", "desc")
        limit = self._safe_limit(params.get("limit", 10))
        chart_type = params.get("chart_type", "bar")
        chart_title = params.get("chart_title", "Analiz Sonucu")
        description = params.get("description", "")
        filters = params.get("filters") or {}
        having = params.get("having")
        window = params.get("window")
        union = params.get("union")

        if not target_table:
            # Tek tablo varsa bu bir secim degil, zorunluluk. Birden fazlaysa
            # ilkini almak kumardir: LLM tabloyu bilerek bos birakiyor —
            # prompt ona "soruyu karsilayan kolonu bulamazsan target_table'i
            # bos birak" diyor. Onceden `tables[0]` aliniyordu, yani sistem
            # "anlayamadim" dedigi anda rastgele bir tabloyu analiz edip
            # sonucu dogruymus gibi gosteriyordu.
            # Sozlukteki `tables` normalde nesne listesi ama modelin duz ad
            # listesi dondurdugu de oluyor; burada patlamak yerine ikisini de
            # kabul ediyoruz.
            tables = []
            for entry in config.get("tables") or []:
                name = entry.get("name") if isinstance(entry, dict) else entry
                if name:
                    tables.append(str(name))
            if len(tables) == 1:
                target_table = tables[0]
            elif len(tables) > 1:
                return self._error_result(
                    "Sorunun hangi tabloyla ilgili olduğu anlaşılamadı. Analiz "
                    f"kapsamındaki tablolar: {', '.join(tables)}. Sorunuzda "
                    "tablo ya da alan adını belirtir misiniz?")

        if not target_table:
            return self._error_result("Hedef tablo belirlenemedi")

        # LLM'in verdigi kararlar denetim izine yaziliyor: kalite olcumunde
        # asil bakilacak yer burasi.
        self.audit = {
            "analysisType": analysis_type,
            "targetTable": target_table,
            "targetColumn": target_column,
            "groupBy": group_by,
            "aggregation": aggregation,
            "filters": filters,
            "sortBy": sort_by,
            "sortOrder": sort_order,
            "limit": limit,
        }

        try:
            self._selection.preflight(self, config, params, target_table)
        except ValueError as error:
            return self._with_audit(self._error_result(str(error)))
        if self.audit.get("pendingConfirmations"):
            message = "Relationship confirmation is required before analysis can execute."
            return self._with_audit({
                **self._error_result(message), "needsClarification": True,
                "pendingConfirmations": self.audit["pendingConfirmations"],
            })

        # Each UNION branch resolves its own joins and column scope.
        if union:
            if analysis_type != "aggregation":
                return self._with_audit(self._error_result(
                    "Birleşim yalnızca gruplama sorularında kullanılabilir."))
            # Pencere hesabi hala reddediliyor ve bu bilincli. Birikim ya da
            # hareketli ortalama, birlesimin TAMAMI uzerinde anlamli; dal
            # basina hesaplanirsa iki ayri birikim cikar ve grafik "kumulatif"
            # dendigi halde kumulatif olmayan bir sey gosterir. Sessizce
            # yanlis bir seri uretmektense acikca reddetmek dogru.
            if window:
                return self._with_audit(self._error_result(
                    "Birleşimle birlikte birikim/hareketli ortalama "
                    "hesaplanamıyor: bu hesaplar dal başına değil birleşimin "
                    "tamamı üzerinde anlamlı. Soruyu tek kaynak üzerinden "
                    "sorun ya da bu hesabı bırakın."))
            try:
                return self._with_audit(self._sql_union(
                    config, union, target_table, group_by, target_column,
                    aggregation, filters, params.get("joins"), having,
                    sort_order, limit, chart_type, chart_title,
                    description))
            except ValueError as e:
                return self._with_audit(self._error_result(str(e)))
            except Exception as e:
                logger.error(f"Birleşim hatası: {e}")
                return self._with_audit(self._error_result(str(e)))

        try:
            schema, table = self._split_table(target_table)
            columns = self._table_columns(schema, table)
        except Exception as e:
            logger.error(f"Tablo cozumlenemedi: {e}")
            return self._with_audit(self._error_result(str(e)))

        aliases = AliasFactory()
        base = TableRef(schema, table, aliases.take())

        # Kolon cozumleme artik takma ad haritasi uzerinden: join geldiginde
        # ayni kolon adi birden fazla tabloda bulunabiliyor ve hangisinin
        # kastedildigi tahmin edilemez.
        scope = ColumnScope()
        scope.add(base.alias, columns,
                  [target_table, f"{schema}.{table}", table], is_base=True)

        # Zincir once kuruluyor: filtreler ve kirilim, baglanan tablolarin
        # kolonlarini da kullanabilsin.
        try:
            joins, join_params, join_notes = self._resolve_joins(
                config, base, target_table, params.get("joins") or [],
                aliases, scope)
        except ValueError as e:
            return self._with_audit(self._error_result(str(e)))

        self.audit["joins"] = join_notes

        # Filtreler kolon adlariyla dogrulanip parametreye baglaniyor.
        # Uygulanamayan filtre sessizce dusurulmuyor: "bu yil" diye sorup
        # tum zamanlarin sonucunu gormek, yanlis cevabin en sinsi hali.
        try:
            predicates, where_params, filter_notes = self._build_where(filters, scope)
            where_params.update(join_params)
        except ValueError as e:
            return self._with_audit(self._error_result(str(e)))

        self.audit["appliedFilters"] = filter_notes
        self._record_learned_codes(config, filters, scope)

        try:
            # Gruplama/sayma sorulari SQL'de calisir: sonuc tablonun tamami
            # uzerinden ve kesindir. Onceden tablodan TOP 50000 satir cekilip
            # gruplama pandas'ta yapiliyordu — tablo bundan buyukse "en cok
            # gidilen 5 ulke" keyfi bir 50 binlik dilimin siralamasi oluyordu.
            if analysis_type == "aggregation":
                return self._with_audit(self._sql_aggregation(
                    base, joins, scope, target_column, group_by, aggregation,
                    sort_order, limit, predicates, where_params, chart_type,
                    chart_title, description, having, window))

            # Kalan tipler (PyCaret modelleri, korelasyon, ozet istatistik)
            # satir bazli veri istiyor; bunlar icin okuma tavani kacinilmaz.
            # Bunlarda join kurulmuyor: `t0.*` yalnizca taban tabloyu
            # projekte ediyor ve model egitimi zaten tek tablo uzerinden
            # tanimlanmis durumda.
            if joins:
                return self._with_audit(self._error_result(
                    "Bu analiz türü tek tablo üzerinde çalışıyor; tabloları "
                    "birleştirerek yapılamıyor."))

            for raw in ([target_column] if target_column else []) + feature_columns:
                if scope.resolve(raw) is None:
                    raise ValueError(self._column_missing(raw, scope))
            df = self._load_table_data(base, predicates, where_params, self.MAX_ROWS)

            if df.empty:
                return self._with_audit(
                    self._error_result(f"'{target_table}' tablosunda veri bulunamadı"))

            # Analiz tipine göre işlem yap
            if analysis_type == "correlation":
                result = self._correlation_analysis(df, chart_title, description)
            elif analysis_type == "statistics":
                result = self._statistics_analysis(df, target_column, chart_type, chart_title, description)
            elif analysis_type == "regression":
                result = self._regression_analysis(df, target_column, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "classification":
                result = self._classification_analysis(df, target_column, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "anomaly":
                result = self._anomaly_analysis(df, feature_columns, chart_type, chart_title, description)
            elif analysis_type == "clustering":
                result = self._clustering_analysis(df, feature_columns, chart_type, chart_title, description)
            else:
                return self._with_audit(self._error_result(
                    f"Bilinmeyen analiz tipi: '{analysis_type}'."))

            return self._with_audit(result)

        except Exception as e:
            logger.error(f"Analiz hatası: {e}")
            return self._with_audit(self._error_result(str(e)))

    def _with_audit(self, result: Dict[str, Any]) -> Dict[str, Any]:
        """
        Denetim izini sonuca ekler. Basarili da olsa basarisiz da olsa
        eklenir: kalite olcumunde asil ihtiyac duyulan an, sonucun yanlis
        goründügü andir.
        """
        if result.get("success"):
            try:
                validate_charts(result.get("charts"))
            except ValueError as error:
                result = self._error_result(str(error))
        result["audit"] = dict(self.audit)

        # Okuma tavanina degildiyse sonuc tablonun tamamini temsil etmiyor.
        # Bunu kullaniciya soylemek sart: dogru gorunen ama eksik veriden
        # cikmis bir grafik, acikca basarisiz olan bir sorgudan daha zararli.
        if self.audit.get("truncated") and result.get("success"):
            readable = f"{self.MAX_ROWS:,}".replace(",", ".")
            result.setdefault("insights", []).insert(0, {
                "type": "warning",
                "title": "Kısmi veri",
                "description": (
                    f"Tablodan yalnızca ilk {readable} satır okundu. Bu analiz "
                    "tablonun tamamını temsil etmiyor olabilir; sonucu daraltmak "
                    "için tarih ya da kategori filtresi ekleyin."
                )
            })

        return result

    # ------------------------------------------------------------------
    # Sema cozumleme
    #
    # LLM'in verdigi her tablo/kolon adi calistirilmadan once gercek semaya
    # karsi dogrulanir. Iki isi birden yapiyor: adin SQL'e gomulmesini
    # guvenli kiliyor (yalnizca semadan gelen ad yaziliyor) ve ad tutmadiginda
    # "tabloda yok, mevcut kolonlar sunlar" diyebilmeyi sagliyor.
    # ------------------------------------------------------------------

    ANALYSIS_TYPES = (
        "aggregation", "statistics", "correlation",
        "regression", "classification", "anomaly", "clustering",
    )

    # Bir olcum kolonu isteyen toplulastirmalar. SQL karsiliklari
    # `query_spec.Aggregate` icinde; burada yalnizca "kolon sart mi" sorusu
    # cevaplaniyor.
    _NUMERIC_AGGS = frozenset({"sum", "avg", "min", "max"})

    @staticmethod
    def _split_table(table_name: str) -> tuple:
        return table_parts(table_name)

    #: Zincirde en fazla kac adim. Finans sorgulari 7-8 tabloya kadar
    #: cikabiliyor; ustu neredeyse her zaman modelin yolunu kaybettigine isaret.
    MAX_JOINS = 8

    def _resolve_joins(self, config: Dict, base: TableRef, base_table: str,
                       requested: List[Dict], aliases: AliasFactory,
                       scope: ColumnScope, param_scope: str = "") -> tuple:
        """
        Modelin istedigi zinciri, olculmus iliskilere dayanarak kurar.

        Model yalnizca HANGI tabloyu NEREYE baglamak istedigini soyler.
        Eslesen kolonlar, kardinalite ve join tipi sozlukteki `relationships`
        kaydindan okunur — modelden degil. Bu ayrim emniyetin tamami: kardinalite
        modelden gelseydi, model 1:N kontrolunden gecmek icin "many-to-one"
        yazar ve sayilar sessizce siserdi.

        Doner: (joins, params, notes)
        """
        edges = config.get("relationships") or []
        if requested and not edges:
            raise ValueError(
                "Bu soru birden fazla tablo gerektiriyor ama bağlantılar "
                "çıkarılmamış. 'Analiz Et' ile bağlantıları keşfedebilir veya "
                "eşleşen tablo ve kolonların tam adlarını belirtip ilişkiyi "
                "onaylayabilirsiniz. Beyan edilen ilişki, doğrulamayı geçip "
                "analiz sözlüğüne eklendiğinde kullanılabilir.")

        if len(requested) > self.MAX_JOINS:
            raise ValueError(
                f"Soru {len(requested)} tablonun birleştirilmesini gerektiriyor; "
                f"en fazla {self.MAX_JOINS} tabloya kadar birleştirebiliyorum.")

        joins: List[Join] = []
        params: Dict[str, Any] = {}
        notes: List[str] = []
        # `as` adi -> (takma ad, tam tablo adi). Zincirin bir sonraki adimi
        # buradan hangi tabloya baglanacagini buluyor.
        steps: Dict[str, tuple] = {"base": (base.alias, base_table)}
        idx = 0

        for step in requested:
            if not isinstance(step, dict):
                continue

            target = step.get("table")
            if not target:
                raise ValueError("Birleştirilecek tablo adı boş geldi.")

            name = str(step.get("as") or "").strip() or f"j{idx}"
            source_key = str(step.get("from") or "base").strip()

            if source_key not in steps:
                raise ValueError(
                    f"'{target}' tablosu '{source_key}' adımına bağlanmak "
                    "istiyor ama böyle bir adım yok.")
            if name in steps:
                raise ValueError(f"'{name}' adı zincirde iki kez kullanılmış.")

            source_alias, source_table = steps[source_key]
            edge, reversed_edge = self._find_edge(
                edges, source_table, target, step.get("via"))

            if edge.get("needsConfirmation"):
                raise ValueError("Relationship confirmation is required before execution.")
            self._record_pending(edge)
            self._record_evidence(edge)

            schema, table = self._split_table(target)
            target_columns = self._table_columns(schema, table)

            # Kenar cocuktan ebeveyne normalize edilmis. Ters yonde kullanmak
            # (taban ebeveyn tarafinda) bire-cok demektir ve satirlari cogaltir.
            pre_agg = step.get("preAggregate")
            if reversed_edge and not isinstance(pre_agg, dict):
                # Olcu belirtilmemis bire-cok tablo: anahtar basina SAYIM tek
                # guvenli varsayim. Uydurulmus bir toplam yerine sayim,
                # ebeveynin kendi sayilarini bozmayan tek secenek.
                pre_agg = {"aggregation": "count", "column": None}
                notes.append(
                    f"{target} bire-çok bağlı; kayıt sayısı olarak ön toplandı.")

            step_filter = step.get("filter")
            if reversed_edge and isinstance(step_filter, dict) and step_filter:
                # On toplanan tabloda ayirt edici kosul, toplama ONCE
                # uygulanmali; ON'a konursa once her sey toplanir, sonra
                # filtrelenir ve toplam yanlis cikar.
                raise ValueError(
                    f"'{target}' bire-çok bağlı olduğu için ön toplanıyor; "
                    "bu tabloya ayırt edici koşul veremiyorum.")

            if reversed_edge:
                join, agg_label = self._build_pre_aggregated(
                    edge, schema, table, target_columns, pre_agg,
                    source_alias, aliases, name)
                # Turetilmis tablonun disariya actigi tek olcu bu; kolon
                # haritasini ona gore kuruyoruz ki `projected` denetimi
                # anlamli hata verebilsin.
                columns = {c.lower(): c for c in join.table.output_columns()}
                notes.append(
                    f"{target} → {agg_label} olarak ön toplandı "
                    f"({', '.join(edge.get('fromColumns') or [])} anahtarıyla)")
            else:
                join = self._build_direct(
                    edge, schema, table, source_alias, aliases)
                columns = target_columns

                # Ayirt edici kosul ON'a giriyor, WHERE'e degil: LEFT join'de
                # WHERE eslesmeyen satirlari da eler ve join sessizce INNER'a
                # doner. Deger metne girmiyor, parametreye baglaniyor.
                if isinstance(step_filter, dict) and step_filter:
                    # Parametre adi dal kapsamini tasiyor. Birlesimde dallar
                    # AYNI parametre sozlugunu paylasiyor; iki dalda da
                    # sifirinci adimda filtre varsa ayni ad iki farkli degere
                    # baglanirdi ve biri sessizce otekinin degerini alirdi.
                    extra, filter_params = self._build_join_filter(
                        step_filter, columns, join.table.alias, f"{param_scope}{idx}")
                    join = Join(join.table, list(join.conditions) + extra,
                                cardinality=join.cardinality, kind=join.kind)
                    params.update(filter_params)
                    notes.append(
                        f"{target} ({', '.join(f'{k}={v}' for k, v in step_filter.items())})")
                notes.append(
                    f"{source_table} → {target} "
                    f"({self._edge_source_label(edge)}"
                    f"{self._overlap_note(edge)})")

            scope.add(join.table.alias, columns,
                      [name, target, f"{schema}.{table}", table])
            steps[name] = (join.table.alias, target)
            joins.append(join)
            idx += 1

        return joins, params, notes

    def _build_join_filter(self, step_filter: Dict, columns: Dict[str, str],
                           alias: str, step_index: Any) -> tuple:
        """
        Join'i ayirt eden sabit kosullari `Predicate`e cevirir.

        Ayni parametre tablosuna iki kez baglanirken, hangi baglantinin hangi
        kod grubunu getirdigini yalnizca bu kosullar soyler.
        """
        conditions: List[Predicate] = []
        params: Dict[str, Any] = {}

        for raw_col, value in step_filter.items():
            real = columns.get(str(raw_col).replace("[", "").replace("]", "").strip().lower())
            if real is None:
                raise ValueError(
                    f"Birleştirme koşulu uygulanamadı — '{raw_col}' kolonu "
                    "bağlanan tabloda yok.")

            column = ColumnRef(alias, real)

            if value is None:
                conditions.append(Predicate(column, "IS NULL"))
                continue

            if isinstance(value, (list, tuple, set)):
                items = list(value)
                if not items:
                    raise ValueError(f"'{real}' için boş birleştirme koşulu verildi.")
                keys = []
                for operand in items:
                    key = f"j{step_index}_{len(params)}"
                    params[key] = operand
                    keys.append(key)
                conditions.append(Predicate(column, "IN", keys))
                continue

            key = f"j{step_index}_{len(params)}"
            params[key] = value
            conditions.append(Predicate(column, "=", [key]))

        return conditions, params

    @staticmethod
    def _overlap_note(edge: Dict) -> str:
        overlap = edge.get("valueOverlap")
        if edge.get("source") == "fk" or overlap is None:
            return ""
        try:
            return f", örtüşme %{round(float(overlap) * 100)}"
        except (TypeError, ValueError):
            return ""

    @staticmethod
    def _edge_source_label(edge: Dict) -> str:
        """
        Kenarin nereden geldigi. Denetim panelinde gorunen sey bu ve uc
        kaynagin agirligi ayni degil: veritabaninin zorladigi bir kisit ile
        adlara bakip tahmin edilmis bir eslesme arasindaki farki kullanicinin
        gormesi gerekiyor.
        """
        return {
            "fk": "doğrulanmış",
            "declared": "sizin kurduğunuz",
        }.get(edge.get("source"), "çıkarsanmış")

    #: Kanit dereceleri, GUCLUDEN ZAYIFA. Sira onemli: bir cevabin ne kadar
    #: saglam durdugunu, kullandigi EN ZAYIF baglanti belirliyor.
    #:
    #:   fk       — veritabaninin kendisi bu iliskiyi zorluyor.
    #:   inferred — adlar ortusuyor VE degerler tutuyor; ikisi de olculdu.
    #:   declared — veritabanini bilen bir insan boyle oldugunu soyledi.
    #:              Hedefin benzersizligi yine olculdu ama dusuk ortusme
    #:              engellemiyor, yalnizca uyariyor.
    _EVIDENCE_ORDER = ["fk", "inferred", "declared"]

    def _record_evidence(self, edge: Dict) -> None:
        """
        Cevabin dayandigi en zayif kaniti denetim izine yaziyor.

        Neden en zayifi: sekiz tabloyu birlestiren bir sorguda yedisi
        bildirilmis yabanci anahtar, biri kullanicinin beyani olabilir. O
        cevap "dogrulanmis" degildir — zinciri en zayif halkasi tasir.

        Denetim panelini ACMADAN gorunmesi gerekiyor. Panel varken bile
        acilmiyor; acilmayan bir panelde duran uyari, uyari degildir.
        """
        source = edge.get("source")
        if source not in self._EVIDENCE_ORDER:
            source = "inferred"

        current = self.audit.get("evidence")
        if current is None or (self._EVIDENCE_ORDER.index(source)
                               > self._EVIDENCE_ORDER.index(current)):
            self.audit["evidence"] = source

    def _record_learned_codes(self, config: Dict, filters: Any,
                              scope: ColumnScope) -> None:
        """
        Kullanicinin OGRETTIGI bir kod anlamina dayanildiysa denetim izine
        yaziyor: "'ROD' karayolu demek" bilgisi sizden geldi ve bu cevap ona
        dayaniyor.

        Yalnizca kodlar izlenebiliyor, es anlamlilar izlenemiyor. Sebep
        durust olmayi gerektiriyor: "gelir" kelimesini EarningAmount'a
        baglayan sey modelin prompt icindeki sessiz karari; hangi es
        anlamliyi kullandigini bize soylemiyor. Bilmedigimiz bir seyi
        "kullanildi" diye yazmak, denetim izinin degerini bitirir.

        Iki sart birden araniyor — kolon adi VE deger — cunku yanlis bir
        "sizin ogrettiginiz bilgi kullanildi" notu, hic not olmamasindan
        kotudur.
        """
        if not isinstance(filters, dict) or not filters:
            return

        entries = [e for e in (config.get("codeValues") or [])
                   if isinstance(e, dict) and isinstance(e.get("meanings"), dict)]
        if not entries:
            return

        used: List[str] = []

        for raw_column, raw_value in filters.items():
            resolved = scope.resolve(raw_column)
            if resolved is None:
                continue

            # Filtre tek deger ya da liste olabilir; ikisi de ayni sekilde
            # aranıyor. Aralik filtreleri ({gte, lt}) kod olamaz.
            values = raw_value if isinstance(raw_value, (list, tuple, set)) else [raw_value]

            for entry in entries:
                if str(entry.get("column") or "").lower() != resolved.name.lower():
                    continue
                for value in values:
                    meaning = entry["meanings"].get(str(value))
                    if meaning is None:
                        continue
                    note = f"{resolved.name} = '{value}' → {meaning}"
                    if note not in used:
                        used.append(note)

        if used:
            self.audit["learnedCodes"] = used

    def _record_pending(self, edge: Dict) -> None:
        """Collect proposals during preflight; never execute pending edges."""
        if not edge.get("needsConfirmation"):
            return

        from_columns = edge.get("fromColumns") or []
        to_columns = edge.get("toColumns") or []
        if not from_columns or not to_columns:
            return

        pending = self.audit.setdefault("pendingConfirmations", [])

        record = {
            "fromTable": edge.get("fromTable"),
            "fromColumn": from_columns[0],
            "fromColumns": from_columns,
            "toTable": edge.get("toTable"),
            "toColumn": to_columns[0],
            "toColumns": to_columns,
            "valueOverlap": edge.get("valueOverlap"),
        }

        # Ayni kenar zincirde iki kez gecebilir; kullaniciya iki kez
        # sorulmamali.
        if record not in pending:
            pending.append(record)

    def _find_edge(self, edges: List[Dict], source_table: str,
                   target_table: str, via: Optional[str]) -> tuple:
        """
        Iki tablo arasindaki olculmus baglantiyi bulur.

        Doner: (kenar, ters_mi). `ters_mi` True ise hedef tablo cocuk
        tarafinda, yani baglanti bire-cok.
        """
        source = self._qualified(source_table)
        target = self._qualified(target_table)

        forward = [e for e in edges
                   if self._qualified(e.get("fromTable")) == source
                   and self._qualified(e.get("toTable")) == target]
        backward = [e for e in edges
                    if self._qualified(e.get("fromTable")) == target
                    and self._qualified(e.get("toTable")) == source]

        if via:
            wanted = str(via).lower()

            def matches(edge: Dict) -> bool:
                columns = (edge.get("fromColumns") or []) + (edge.get("toColumns") or [])
                return any(str(c).lower() == wanted for c in columns)

            forward = [e for e in forward if matches(e)]
            backward = [e for e in backward if matches(e)]

        total = len(forward) + len(backward)
        if total == 0:
            # A declaration still needs validation; spelling alone proves no edge.
            raise ValueError(
                f"'{source_table}' ile '{target_table}' arasında ölçülmüş bir "
                "bağlantı yok veya belirtilen bağlantı kolonu kayıtla eşleşmiyor. "
                "Eşleşen kolonları tablo adlarıyla ve gerçek yazımlarıyla "
                "belirtip ilişkiyi onaylayabilirsiniz; örneğin ReferenceId "
                "ve ReferanceId farklı kolon adlarıdır, birbirinin yerine "
                "kullanılmaz. Beyan edilen ilişki, doğrulamayı geçip analiz "
                "sözlüğüne eklendiğinde kullanılabilir; yabancı anahtar "
                "zorunlu değildir. Mevcut bağlantıları keşfetmek için "
                "'Analiz Et'i de kullanabilirsiniz.")
        if total > 1:
            candidates = ", ".join(
                ", ".join(e.get("fromColumns") or []) for e in forward + backward)
            raise ValueError(
                f"'{source_table}' ile '{target_table}' arasında birden fazla "
                f"bağlantı var ({candidates}). Hangisini kastettiğinizi "
                "belirtmeniz gerekiyor.")

        return (forward[0], False) if forward else (backward[0], True)

    @staticmethod
    def _qualified(name: Optional[str]) -> str:
        if not name:
            return ""
        return ".".join(table_parts(name)).casefold()

    @staticmethod
    def _build_direct(edge: Dict, schema: str, table: str,
                      source_alias: str, aliases: AliasFactory) -> Join:
        """N:1 baglanti — cogalma yok, dogrudan baglanir."""
        target = TableRef(schema, table, aliases.take())
        from_columns = edge.get("fromColumns") or []
        to_columns = edge.get("toColumns") or []

        if not from_columns or len(from_columns) != len(to_columns):
            raise ValueError(
                f"'{schema}.{table}' bağlantısının eşleşme kolonları eksik.")

        conditions = [
            JoinCondition(ColumnRef(source_alias, left), ColumnRef(target.alias, right))
            for left, right in zip(from_columns, to_columns)
        ]

        # INNER yalnizca BILDIRILMIS, veritabaninin dogruladigi ve bos
        # gecilemeyen bir FK icin guvenli; digerlerinde eslesmeyen taban
        # satirlari sessizce duser ve sayilar degisir. Cikarsanmis kenar ne
        # kadar yuksek ortusme gosterirse gostersin bu garantiyi vermez —
        # olcum bir orneklem uzerinde yapiliyor.
        trusted = (edge.get("source") == "fk"
                   and bool(edge.get("isTrusted"))
                   and not bool(edge.get("isOptional", True)))
        return Join(target, conditions, cardinality=MANY_TO_ONE,
                    kind="inner" if trusted else "left")

    def _build_pre_aggregated(self, edge: Dict, schema: str, table: str,
                              columns: Dict[str, str], pre_agg: Dict,
                              source_alias: str, aliases: AliasFactory,
                              name: str) -> tuple:
        """
        Bire-cok tabloyu once kendi anahtarina gore toplar, sonra baglar.

        Boylece anahtar basina tek satir kalir ve dis sorguya N:1 baglanir —
        cogalma hic olusmaz. "Kac fatura" ile "kalem tutari toplami" ayni
        sorguda, ikisi de dogru cikar.
        """
        key_names = edge.get("fromColumns") or []
        parent_names = edge.get("toColumns") or []
        if not key_names or len(key_names) != len(parent_names):
            raise ValueError(
                f"'{schema}.{table}' bağlantısının eşleşme kolonları eksik.")

        source = TableRef(schema, table, aliases.take())

        real_keys = []
        for key in key_names:
            real = columns.get(str(key).lower())
            if real is None:
                raise ValueError(
                    f"'{schema}.{table}' tablosunda '{key}' anahtar kolonu yok.")
            real_keys.append(real)

        func = str(pre_agg.get("aggregation") or "count").strip().lower()
        raw_column = pre_agg.get("column")
        measure = columns.get(str(raw_column).lower()) if raw_column else None

        if func in self._NUMERIC_AGGS and measure is None:
            raise ValueError(
                f"'{schema}.{table}' tablosunda '{func}' işlemi için hangi "
                f"alanın hesaplanacağı belirtilmemiş.")
        if func not in self._NUMERIC_AGGS:
            func, measure = "count", None

        label = f"{name}_{func}"
        derived = DerivedTable(
            source=source,
            key_columns=[ColumnRef(source.alias, k) for k in real_keys],
            aggregate=Aggregate(
                func,
                ColumnRef(source.alias, measure) if measure else None,
                label),
            alias=aliases.take(),
        )

        conditions = [
            JoinCondition(ColumnRef(source_alias, parent), ColumnRef(derived.alias, key))
            for parent, key in zip(parent_names, real_keys)
        ]

        return Join(derived, conditions, cardinality=MANY_TO_ONE, kind="left"), label

    @staticmethod
    def _safe_limit(value: Any, default: int = 10) -> int:
        try:
            n = int(value)
        except (TypeError, ValueError):
            return default
        return min(max(n, 1), 1000)

    def _table_columns(self, schema: str, table: str) -> Dict[str, str]:
        """
        Tablonun gercek kolonlari: kucuk harfli ad -> gercek ad.

        Kucuk harfli anahtar kasitli: LLM kolon adini farkli buyuk/kucuk
        harfle yazdiginda sorgu bunun yuzunden dusmesin.
        """
        if self._selection is None:
            raise ValueError("Analysis selection has not been validated.")
        return self._selection.columns(f"{schema}.{table}")

    @staticmethod
    def _resolve_column(name: Optional[str], columns: Dict[str, str]) -> Optional[str]:
        if not name:
            return None
        return columns.get(str(name).replace("[", "").replace("]", "").strip().lower())

    @staticmethod
    def _column_missing(name: str, scope: "ColumnScope") -> str:
        return (f"'{name}' kolonu sorgudaki tablolarda yok. Mevcut kolonlar: "
                f"{', '.join(scope.known_columns()[:20])}")

    # ------------------------------------------------------------------
    # Filtreler
    # ------------------------------------------------------------------

    _RANGE_OPS = {"gte": ">=", "gt": ">", "lte": "<=", "lt": "<", "eq": "=", "ne": "<>"}

    def _build_where(self, filters: Dict, scope: ColumnScope,
                     prefix: str = "p") -> tuple:
        """
        Filtreleri `Predicate` listesine cevirir.

        Deger SQL metnine hicbir zaman girmez: onceden `[col] = 'val'` diye
        birlestiriliyordu — hem enjeksiyon yuzeyi hem de tarih/sayi
        kolonlarinda sessiz tip hatasi demekti. Artik yalnizca parametre adi
        tasiniyor, degerler ayri sozlukte.

        Cozulemeyen filtre sessizce atlanmaz, hata olur: "bu yil" diye sorup
        tum zamanlarin sonucunu almak yanlis cevabin en fark edilmesi zor hali.

        Kabul edilen bicimler:
            "Ulke": "Almanya"                     -> esitlik
            "Ulke": ["Almanya", "Hollanda"]       -> IN
            "Tarih": {"gte": "2026-01-01", "lt": "2027-01-01"}  -> aralik
        """
        if not filters:
            return [], {}, []

        predicates: List[Predicate] = []
        params: Dict[str, Any] = {}
        notes: List[str] = []
        idx = 0

        for raw_col, value in filters.items():
            column = scope.resolve(raw_col)
            if column is None:
                raise ValueError(
                    f"Filtre uygulanamadı — {self._column_missing(str(raw_col), scope)}")
            real = column.name

            if isinstance(value, dict):
                for op, operand in value.items():
                    sql_op = self._RANGE_OPS.get(str(op).lower())
                    if sql_op is None:
                        raise ValueError(
                            f"'{real}' filtresinde tanınmayan karşılaştırma: '{op}'.")
                    key = f"{prefix}{idx}"; idx += 1
                    predicates.append(Predicate(column, sql_op, [key]))
                    params[key] = operand
                    notes.append(f"{real} {sql_op} {operand}")

            elif isinstance(value, (list, tuple, set)):
                items = list(value)
                if not items:
                    raise ValueError(f"'{real}' için boş filtre listesi verildi.")
                keys = []
                for operand in items:
                    key = f"{prefix}{idx}"; idx += 1
                    keys.append(key)
                    params[key] = operand
                predicates.append(Predicate(column, "IN", keys))
                notes.append(f"{real} IN ({', '.join(str(i) for i in items)})")

            elif value is None:
                predicates.append(Predicate(column, "IS NULL"))
                notes.append(f"{real} boş")

            else:
                key = f"{prefix}{idx}"; idx += 1
                predicates.append(Predicate(column, "=", [key]))
                params[key] = value
                notes.append(f"{real} = {value}")

        return predicates, params, notes

    # ------------------------------------------------------------------
    # Toplulastirma sonrasi kosul ve pencere
    # ------------------------------------------------------------------

    #: Modelin yazabilecegi karsilastirmalar. Metin dogrudan SQL'e girmiyor;
    #: bu sozlukten gecmeyen hicbir sey koşula donusmuyor.
    _HAVING_OPS = {">": ">", ">=": ">=", "<": "<", "<=": "<=",
                   "=": "=", "==": "=", "!=": "<>", "<>": "<>"}

    #: Birikimli seri dondururken uygulanan tavan. Seride TOP, "en yuksek N"
    #: degil "ilk N donem" demek; yuksek tutulmasinin sebebi bu.
    SERIES_LIMIT = 500

    def _build_having(self, having: Any, aggregate: Aggregate,
                      prefix: str = "h", scope: Optional[ColumnScope] = None) -> tuple:
        """
        Toplulastirma sonrasi kosullari `HavingPredicate`e cevirir.

        En yalin bicimde kosul sorgunun KENDI olcusune uygulanir — "toplami 1
        milyonu gecen musteriler". Ama tek olcu yetmiyor: "cirosu 1 milyonu
        gecen AMA siparis sayisi 5'ten az olan musteriler" iki ayri hesap
        istiyor ve ikincisi grafikte gosterilmiyor, yalnizca eliyor.

        Bu yuzden kosul kendi islemini ve kolonunu soyleyebiliyor:

            {"op": ">", "value": 1000000}                         kendi olcusu
            {"aggregation": "count", "op": "<", "value": 5}       satir sayisi
            {"column": "Tutar", "aggregation": "sum", "op": ">",  baska kolon
             "value": 1000000}

        Birden fazla kosul liste olarak verilir ve VE ile baglanir; "ama"
        kelimesinin karsiligi bu.

        Kosulda gecen kolonun kirilimda ya da olcude olmasi gerekmiyor —
        HAVING grubun tamami uzerinde hesaplaniyor ve bu gecerli SQL.
        `validate` kolonun kapsamda oldugunu ayrica denetliyor.

        Doner: (kosullar, parametreler, denetim notu)
        """
        if not having:
            return [], {}, None

        conditions = having if isinstance(having, list) else [having]

        predicates: List[HavingPredicate] = []
        params: Dict[str, Any] = {}
        notes: List[str] = []

        for index, raw in enumerate(conditions):
            if isinstance(raw, (int, float)) and not isinstance(raw, bool):
                # "having": 1000000 — kastedilen neredeyse her zaman
                # "bundan buyuk".
                raw = {"op": ">", "value": raw}

            if not isinstance(raw, dict):
                raise ValueError(
                    "Toplulaştırma sonrası koşul anlaşılamadı; "
                    "{\"op\": \">\", \"value\": 1000} biçiminde olmalı.")

            op = self._HAVING_OPS.get(str(raw.get("op") or "").strip())
            if op is None:
                raise ValueError(
                    f"'{raw.get('op')}' tanınmayan bir karşılaştırma. "
                    f"Kullanılabilir: {', '.join(sorted(set(self._HAVING_OPS)))}.")

            value = raw.get("value")
            if isinstance(value, bool) or not isinstance(value, (int, float)):
                raise ValueError(
                    "Toplulaştırma sonrası koşulun değeri bir sayı olmalı; "
                    "koşul hesaplanmış ölçüye uygulanıyor.")

            measure, label = self._having_measure(raw, aggregate, scope, index)

            key = f"{prefix}{index}"
            params[key] = value
            predicates.append(HavingPredicate(measure, op, [key]))
            notes.append(f"{label} {op} {value}")

        return predicates, params, " ve ".join(notes)

    def _having_measure(self, condition: Dict, default: Aggregate,
                        scope: Optional[ColumnScope], index: int) -> tuple:
        """
        Bir kosulun uzerinde calisacagi olcu. Islem ve kolon verilmemisse
        sorgunun kendi olcusu kullaniliyor — bugunku davranis.

        Doner: (olcu, denetim izinde gorunecek etiket)
        """
        wanted_agg = str(condition.get("aggregation") or "").strip().lower()
        wanted_col = condition.get("column")

        if not wanted_agg and not wanted_col:
            label = default.label if default.column is None else (
                f"{default.func.upper()}({default.column.name})")
            return default, label

        func = wanted_agg or default.func
        if func not in self._NUMERIC_AGGS and func != "count":
            raise ValueError(
                f"'{func}' koşulda kullanılamaz. Kullanılabilir: "
                f"count, {', '.join(sorted(self._NUMERIC_AGGS))}.")

        column = None
        if wanted_col:
            if scope is None:
                raise ValueError(
                    "Koşulda kolon belirtilemez: bu sorgu tipinde kolonlar "
                    "çözümlenemiyor.")
            column = scope.resolve(wanted_col)
            if column is None:
                raise ValueError(
                    f"Koşulda geçen alan bulunamadı — "
                    f"{self._column_missing(str(wanted_col), scope)}")
        elif func != "count":
            # COUNT disindaki islemler bir kolon ister; kolonsuz SUM
            # anlamsizdir. Sorgunun kendi olcu kolonu varsa o kullaniliyor.
            column = default.column
            if column is None:
                raise ValueError(
                    f"'{func}' koşulu için hangi alanın hesaplanacağı "
                    "belirtilmemiş.")

        label = f"{func.upper()}({column.name})" if column is not None else "COUNT(*)"
        # Etiket yalnizca denetim izi icin; HAVING'in ciktida bir kolonu yok.
        return Aggregate(func, column, f"_h{index}"), label

    def _build_window(self, window: Any, aggregate: Aggregate,
                      group_refs: List[ColumnRef], direction: str) -> tuple:
        """
        Pencere istegini `WindowExpr`e cevirir.

        Model ham pencere sozdizimi yazmiyor, adlandirilmis birkac kalip
        seciyor. Sebebi cerceve: sirali bir birikimde cerceve yazilmazsa SQL
        varsayilani esit siralama degerlerini tek satirda toplar ve bu
        grafikte hic gorunmez. Kalibi kuran taraf cerceveyi de kuruyor;
        boylece modelin unutabilecegi bir sey kalmiyor.

        Doner: (pencereler, denetim notu, seri_mi)

        `seri_mi` — birikim ve hareketli ortalama, satirlarin KENDI
        siralamasinda okunur. Sonucu olcuye gore siralamak dogru hesaplanmis
        bir birikimi okunamaz hale getirir.
        """
        if not window:
            return [], None, False

        if isinstance(window, str):
            window = {"function": window}
        if not isinstance(window, dict):
            raise ValueError("Pencere hesabı bir nesne olmalı.")

        kind = str(window.get("function") or "").strip().lower()
        label = str(window.get("label") or "").strip()

        if kind in ("rank", "row_number", "dense_rank"):
            return ([WindowExpr(func=kind, label=label or "Sıra",
                                order_by=[OrderBy(aggregate, direction)])],
                    f"{kind} — ölçüye göre", False)

        if kind == "total":
            return ([WindowExpr(func="sum", label=label or "Genel toplam",
                                over=aggregate)],
                    "genel toplam", False)

        if kind in ("running_total", "moving_average"):
            if not group_refs:
                raise ValueError(
                    "Birikim hesabı için bir kırılım gerekiyor: neyin üzerinde "
                    "biriktiğini söylemeden birikim tanımlanamaz.")
            order = [OrderBy(group_refs[0], "asc")]

            # Ilk kirilim serinin ekseni (genellikle zaman); geri kalanlar
            # BOLUM oluyor. "Aylara gore, ulke bazinda birikimli ciro"
            # sorusunda dogru cevap ulke basina birikimdir — hepsini tek
            # siraya dizmek, ulkeleri birbirinin uzerine toplar. Ayrica bolum
            # olmadan esit eksen degerlerinde satir sirasi belirsiz kalir ve
            # ayni soru iki farkli birikim dondurebilir.
            partition = list(group_refs[1:])
            scoped = (f" · {', '.join(c.name for c in partition)} bazında"
                      if partition else "")

            if kind == "running_total":
                return ([WindowExpr(func="sum", label=label or "Birikimli",
                                    over=aggregate, partition_by=partition,
                                    order_by=order, frame=FRAME_CUMULATIVE)],
                        f"birikimli toplam — {group_refs[0].name} sırasıyla{scoped}",
                        True)

            periods = window.get("periods")
            if isinstance(periods, bool) or not isinstance(periods, int) or periods < 2:
                raise ValueError(
                    "Hareketli ortalama kaç dönemi kapsayacağını bilmiyor; "
                    "'periods' 2 ya da daha büyük olmalı. Tek dönemlik "
                    "ortalama değerin kendisidir.")
            return ([WindowExpr(func="avg",
                                label=label or f"{periods} dönem ortalaması",
                                over=aggregate, partition_by=partition,
                                order_by=order, frame=FRAME_MOVING,
                                preceding=periods - 1)],
                    f"{periods} dönemlik hareketli ortalama — "
                    f"{group_refs[0].name} sırasıyla{scoped}",
                    True)

        raise ValueError(
            f"Bilinmeyen pencere hesabı: '{kind}'. Şunlardan biri olmalı: "
            "running_total, moving_average, total, rank, row_number, dense_rank.")

    # ------------------------------------------------------------------
    # SQL gruplama
    # ------------------------------------------------------------------

    GLOBAL_RESULT_LABEL = "Genel sonuç"

    def _sql_aggregation(self, base: TableRef, joins: List[Join],
                         scope: ColumnScope,
                         target_col: Optional[str], group_by: List[str],
                         aggregation: str, sort_order: str, limit: int,
                         predicates: List[Predicate], where_params: Dict,
                         chart_type: str, title: str, desc: str,
                         having: Any = None, window: Any = None) -> Dict:
        """
        Gruplama ve toplama — veritabaninda.

        Sonuc tablonun TAMAMI uzerinden hesaplanir. Bu metot pandas'taki
        eskisinin yerini aliyor: orada once TOP 50000 satir cekiliyor, gruplama
        o dilimde yapiliyordu. Tablo bundan buyukse "en cok gidilen 5 ulke"
        sorusunun cevabi, tablonun siralamasi bile garanti olmayan keyfi bir
        kesitinin siralamasi oluyordu — ve hicbir yerde bu yazmiyordu.
        """
        try:
            group_refs: List[ColumnRef] = []
            for col in group_by:
                ref = scope.resolve(col)
                if ref is None:
                    return self._error_result(
                        f"Gruplama yapılamadı — {self._column_missing(str(col), scope)}")
                group_refs.append(ref)

            target_ref = scope.resolve(target_col)
        except ValueError as e:
            # Belirsiz kolon adi: birden fazla tabloda ayni ad var.
            return self._error_result(str(e))

        agg_key = str(aggregation or "count").strip().lower()
        real_target = target_ref.name if target_ref else None

        if target_col and target_ref is None:
            return self._error_result(
                f"Hesaplanacak alan bulunamadı — {self._column_missing(str(target_col), scope)}")

        if agg_key in self._NUMERIC_AGGS:
            if real_target is None:
                return self._error_result(
                    f"'{agg_key}' işlemi için hangi alanın hesaplanacağı "
                    "belirtilmemiş. Örneğin \"tutara göre toplam\" diyebilirsiniz.")
            value_label = f"{agg_key.upper()}({real_target})"
        else:
            # Sayma: belirli bir kolon verildiyse o kolonun dolu oldugu
            # satirlar, verilmediyse tum satirlar sayilir.
            agg_key = "count"
            value_label = "Adet"

        # Sonuc kolonunun adi sorgudaki hicbir kolonla cakismamali; cakisirsa
        # pandas iki ayni adli kolon gorur ve grafige yanlis seri girer.
        taken = {c.lower() for c in scope.known_columns()}
        alias = "value"
        while alias.lower() in taken:
            alias = "_" + alias

        direction = "asc" if str(sort_order).lower() == "asc" else "desc"
        measure = Aggregate(agg_key, target_ref, alias)

        try:
            having_predicates, having_params, having_note = self._build_having(
                having, measure, scope=scope)
            windows, window_note, is_series = self._build_window(
                window, measure, group_refs, direction)
        except ValueError as e:
            return self._error_result(str(e))

        # Kosulun degeri de bagli parametre; cagiranin sozlugunu bozmadan
        # ustune ekleniyor.
        where_params = {**where_params, **having_params}

        if not group_refs:
            # A global aggregate has at most one row; no fabricated grouping or TOP.
            order: List[OrderBy] = []
            effective_limit = None
        elif is_series:
            # Birikim ve hareketli ortalama satirlarin KENDI sirasinda okunur.
            # Sonucu olcuye gore siralamak, dogru hesaplanmis bir birikimi
            # okunamaz hale getirir: kolon dogru cikar, grafik anlamsiz.
            #
            # Ayni sebeple "ilk 10" tavani da kalkiyor: bir zaman serisinde
            # TOP 10, "en yuksek 10" degil "ilk 10 donem" demek ve seriyi
            # sessizce kesmek, bu kod tabaninin kacinmaya calistigi seyin ta
            # kendisi. Yerine cok daha yuksek bir emniyet tavani konuyor.
            #
            # Siralamada eksen SONA aliniyor: pencere ikinci ve sonraki
            # kirilimlara gore bolunuyor, dolayisiyla her bolumun satirlari
            # ard arda ve kendi zaman sirasinda okunmali.
            order = [OrderBy(ref, "asc") for ref in group_refs[1:]]
            order.append(OrderBy(group_refs[0], "asc"))
            effective_limit = self.SERIES_LIMIT
        else:
            # Siralama benzersiz olmali. "En cok gidilen 5 ulke" sorusunda iki
            # ulke esit sayidaysa hangisinin listeye girecegi yalnizca
            # toplulastirmaya gore siralandiginda belirsizdir — ayni soru iki
            # farkli cevap verir. Kirilim kolonlari ikincil siralama olarak
            # ekleniyor.
            order = [OrderBy(alias, direction)]
            order.extend(OrderBy(ref, "asc") for ref in group_refs)
            effective_limit = limit

        spec = QuerySpec(
            base=base,
            joins=joins,
            group_by=group_refs,
            aggregate=measure,
            where=predicates,
            having=having_predicates,
            windows=windows,
            order_by=order,
            limit=effective_limit,
        )

        try:
            sql = render(spec)
            count_sql = render_group_count(spec) if group_refs else None
        except QuerySpecError as e:
            return self._error_result(str(e))

        logger.info(f"SQL: {sql}")

        grouped = self.data.read_sql(sql, where_params)
        total_groups = (
            self.data.scalar(count_sql, where_params)
            if count_sql is not None else len(grouped)
        )

        self.audit["executedSql"] = sql
        self.audit["aggregationPerformedIn"] = "sql"
        self.audit["rowsRead"] = None  # gruplama SQL'de: satir cekilmedi
        self.audit["groupCount"] = int(total_groups or 0)
        self.audit["resolvedGroupBy"] = [r.name for r in group_refs]
        self.audit["resolvedTargetColumn"] = real_target
        if having_note:
            self.audit["having"] = having_note
        if window_note:
            self.audit["window"] = window_note

        if grouped.empty:
            # Kosul yuzunden bosaldiysa bunu soylemek sart: "kayit bulunamadi"
            # kullaniciyi filtrelere bakmaya gonderir, oysa eleyen sey esik.
            return self._error_result(
                f"Toplulaştırma sonucu '{having_note}' koşulunu karşılamadı. "
                "Koşulu ve filtreleri kontrol edin."
                if having_note else
                "Sorguya uyan kayıt bulunamadı. Filtreleri gevşetmeyi deneyin.")

        if group_refs:
            labels = [self._label(v) for v in grouped[group_refs[0].output_name].tolist()]
            values = [round(float(v), 2) for v in grouped[alias].fillna(0).tolist()]
            result_label = f"{group_refs[0].name} bazında {value_label}"
        else:
            labels = [self.GLOBAL_RESULT_LABEL]
            # An empty SUM/AVG is NULL, not a measured zero.
            values = [self._number_or_none(v) for v in grouped[alias].tolist()]
            result_label = f"{self.GLOBAL_RESULT_LABEL}: {value_label}"

        # Pencere kolonlari ayri seri olarak giriyor: "aylik ciro" ile
        # "birikimli ciro" ayni grafikte yan yana okunmali, ikisi ayri
        # grafikte durursa karsilastirilamaz.
        datasets = [{"label": value_label, "data": values}]
        for expr in windows:
            datasets.append({
                "label": expr.label,
                "data": ([round(float(v), 2) for v in grouped[expr.label].fillna(0).tolist()]
                         if group_refs else
                         [self._number_or_none(v) for v in grouped[expr.label].tolist()]),
            })

        charts = [{
            "type": chart_type,
            "title": title or result_label,
            "data": {"labels": labels, "datasets": datasets}
        }]

        # Ilk satirin ne oldugu siralamaya bagli. Seride ne en yuksek ne en
        # dusuk — sadece ilk donem. Onceden burada `direction == "DESC"`
        # karsilastirmasi vardi; `direction` kucuk harfli uretildigi icin
        # hicbir zaman tutmuyor ve en yuksek deger "En Düşük" diye
        # etiketleniyordu.
        if not group_refs:
            displayed_value = values[0] if values[0] is not None else "Veri yok (NULL)"
            headline = (self.GLOBAL_RESULT_LABEL, f"{value_label}: {displayed_value}")
        elif is_series:
            headline = ("İlk Dönem", f"{labels[0]}: {values[0]}")
        else:
            headline = ("En Yüksek" if direction == "desc" else "En Düşük",
                        f"{labels[0]}: {values[0]}")

        scope_insight = {
            "type": "info",
            "title": "Grup Sayısı" if group_refs else "Kapsam",
            "description": (
                f"{len(grouped)} grup gösteriliyor (toplam {total_groups})"
                if group_refs else
                "Koşullara uyan kayıtların tamamı üzerinde hesaplandı."
            ),
        }
        insights = [
            scope_insight,
            {"type": "success", "title": headline[0], "description": headline[1]},
        ]

        if is_series and (total_groups or 0) > self.SERIES_LIMIT:
            # Seri kesildiyse bunu soylemek zorunlu: kesilmis bir birikim
            # dogru gorunur, cunku eksik olan kisim grafikte hic yok.
            insights.insert(0, {
                "type": "warning", "title": "Seri kesildi",
                "description": f"Seri {self.SERIES_LIMIT} dönemle sınırlandı "
                               f"(toplam {total_groups}). Tarih aralığını "
                               "daraltarak tamamını görebilirsiniz.",
            })

        return {
            "success": True,
            "charts": charts,
            "insights": insights,
            "summary": desc or f"{result_label} hesaplandı.",
        }

    # ------------------------------------------------------------------
    # Birlesim
    # ------------------------------------------------------------------

    def _sql_union(self, config: Dict, union: Any, base_table: Optional[str],
                   group_by: List[str], target_col: Optional[str],
                   aggregation: str, filters: Dict, joins: Any, having: Any,
                   sort_order: str, limit: int, chart_type: str, title: str,
                   desc: str) -> Dict:
        """
        Ayni sorunun birden fazla tabloda sorulmasi ve sonuclarin alt alta
        eklenmesi.

        Ne zaman gerekiyor: ayni sey iki tabloda tutuluyor — ithalat ve
        ihracat, gelen ve giden, gecen yilin arsivi ve bu yilin canlisi — ve
        kullanici ikisini bir arada gormek istiyor. Join bunu yapamaz: join
        tablolari YANYANA koyar, birlesim ALT ALTA. Ikisini karistirmak, iki
        farkli soruya ayni cevabi vermek olur.

        Dallar KENDI join zincirini kurabiliyor. Buna ihtiyac var cunku
        birlesimin en dogal sorusu ("ithalat ve ihracati musteri ADINA gore
        kir") adi baska tablodan almayi gerektiriyor ve ad olmadan kirilim
        musteri koduna duser — kullanicinin okuyamayacagi bir grafik.

        Sinir cikti kolonlarinda, join'de degil: hangi zincir kurulursa
        kurulsun dallarin urettigi kolon ADETI ve ADI birebir ayni olmali.
        Cikti adlari ILK dalin kirilimindan aliniyor, sonraki dallar kendi
        kolonlarini o adla etiketliyor. Tutmazsa <c>validate_union</c>
        reddediyor — birlesimde kolonlar SIRAYLA eslesir ve farkli olursa
        sonuc patlamaz, sessizce karisir.
        """
        if isinstance(union, list):
            # Modelin sik yaptigi kisayol: taban dalin etiketi verilmemis.
            union = {"with": union}
        if not isinstance(union, dict):
            raise ValueError("Birleşim isteği bir nesne olmalı.")

        others = union.get("with") or []
        if not isinstance(others, list) or not others:
            raise ValueError(
                "Birleşim en az bir ek tablo ister; tek tabloluk soru "
                "birleşim değildir.")
        if not base_table:
            raise ValueError("Birleşimin ilk dalı için tablo belirlenemedi.")
        if not group_by:
            raise ValueError(
                "Birleşimde kırılım zorunlu: iki kaynağın hangi eksende "
                "karşılaştırıldığı söylenmeden seriler yan yana konamaz.")

        descriptors = [{
            "table": base_table,
            "label": str(union.get("label") or self._short_table(base_table)),
            "group_by": group_by,
            "target_column": target_col,
            "filters": filters,
            "joins": joins or [],
        }]
        for index, other in enumerate(others):
            if not isinstance(other, dict) or not other.get("table"):
                raise ValueError(
                    f"Birleşimin {index + 2}. dalında tablo belirtilmemiş.")
            descriptors.append({
                "table": str(other["table"]),
                "label": str(other.get("label") or self._short_table(other["table"])),
                "group_by": other.get("group_by") or group_by,
                "target_column": other.get("target_column", target_col),
                "filters": other.get("filters") or {},
                # Dalin kendi zinciri. Verilmemisse taban dalinki DENENMIYOR:
                # iki tablonun ayni yoldan gitmesi icin bir sebep yok ve
                # yanlis varsayilan, sessizce bos bir seri uretir.
                "joins": other.get("joins") or [],
            })

        # Etiket sonuclari ayirmanin TEK yolu: iki dal ayni etiketi tasirsa
        # satirlari tek seride birlesir ve karsilastirma anlamini yitirir.
        seen_labels = set()
        for branch in descriptors:
            if branch["label"] in seen_labels:
                raise ValueError(
                    f"'{branch['label']}' etiketi birden fazla dalda "
                    "kullanılmış; kaynaklar ancak farklı etiketlerle ayrılabilir.")
            seen_labels.add(branch["label"])

        agg_key = str(aggregation or "count").strip().lower()
        if agg_key not in self._NUMERIC_AGGS:
            agg_key = "count"

        aliases = AliasFactory()
        params: Dict[str, Any] = {}
        branches: List[UnionBranch] = []
        notes: List[str] = []
        #: Cikti adlari ILK dalin kirilim adlarindan geliyor; sonraki dallar
        #: kendi kolonlarini bu adla etiketliyor.
        output_names: List[str] = []
        value_label = "Adet"

        for index, branch in enumerate(descriptors):
            schema, table = self._split_table(branch["table"])
            columns = self._table_columns(schema, table)

            ref = TableRef(schema, table, aliases.take())
            scope = ColumnScope()
            scope.add(ref.alias, columns,
                      [branch["table"], f"{schema}.{table}", table], is_base=True)

            # Zincir kirilimdan ONCE kuruluyor: kirilim baglanan tablonun bir
            # kolonu olabilir ("musteri adina gore") ve o kolon kapsama
            # girmeden cozulemez.
            branch_joins, join_params, join_notes = self._resolve_joins(
                config, ref, branch["table"], branch["joins"], aliases, scope,
                param_scope=f"u{index}_")
            params.update(join_params)

            group_refs: List[ColumnRef] = []
            for position, raw in enumerate(branch["group_by"]):
                resolved = scope.resolve(raw)
                if resolved is None:
                    raise ValueError(
                        f"'{branch['table']}' tablosunda kırılım yapılamadı — "
                        f"{self._column_missing(str(raw), scope)}")
                if index == 0:
                    output_names.append(resolved.name)
                elif position >= len(output_names):
                    raise ValueError(
                        f"'{branch['table']}' dalında ilk daldan fazla kırılım "
                        "var; dalların çıktı kolonları birebir aynı olmalı.")
                else:
                    # Iki tablo ayni seyi farkli adla tutabiliyor
                    # ("UlkeAdi" / "Country"); cikti adi ilk dalinki oluyor.
                    resolved = ColumnRef(resolved.alias, resolved.name,
                                         label=output_names[position])
                group_refs.append(resolved)

            if index > 0 and len(group_refs) != len(output_names):
                raise ValueError(
                    f"'{branch['table']}' dalının kırılım sayısı ilk daldan "
                    "farklı; dallar birebir aynı kolonları üretmeli.")

            measure_ref = scope.resolve(branch["target_column"])
            if branch["target_column"] and measure_ref is None:
                raise ValueError(
                    f"'{branch['table']}' tablosunda hesaplanacak alan "
                    f"bulunamadı — {self._column_missing(str(branch['target_column']), scope)}")
            if agg_key in self._NUMERIC_AGGS and measure_ref is None:
                raise ValueError(
                    f"'{agg_key}' işlemi için '{branch['table']}' tablosunda "
                    "hangi alanın hesaplanacağı belirtilmemiş.")
            if index == 0 and measure_ref is not None:
                value_label = f"{agg_key.upper()}({measure_ref.name})"

            predicates, branch_params, filter_notes = self._build_where(
                branch["filters"], scope, prefix=f"u{index}_")
            params.update(branch_params)

            label_param = f"src{index}"
            params[label_param] = branch["label"]

            measure = Aggregate(agg_key, measure_ref, "value")

            # Esik her dala AYRI uygulaniyor ve dogrusu bu: "toplami 1M'yi
            # gecen musteriler" sorusu, ithalatta 1M'yi gecen ile ihracatta
            # gecen musterileri ayri ayri sorar. Birlesimden sonra uygulamak
            # iki kaynagin toplamini esige sokmak olurdu — sorulan bu degil.
            branch_having, having_params, having_note = self._build_having(
                having, measure, prefix=f"hu{index}_", scope=scope)
            params.update(having_params)

            branches.append(UnionBranch(
                spec=QuerySpec(
                    base=ref,
                    joins=branch_joins,
                    group_by=group_refs,
                    aggregate=measure,
                    where=predicates,
                    having=branch_having,
                ),
                label_param=label_param))

            detail = list(filter_notes) + list(join_notes)
            if having_note and index == 0:
                notes.append(f"Her kaynakta koşul: {having_note}")
            notes.append(f"{branch['label']} ← {branch['table']}"
                         + (f" ({' · '.join(detail)})" if detail else ""))

        direction = "asc" if str(sort_order).lower() == "asc" else "desc"
        spec = UnionSpec(
            branches=branches,
            order_by=[OrderBy("value", direction),
                      # Siralama benzersiz olmali: esit degerlerde hangi
                      # satirin listeye girecegi yoksa belirsiz kalir.
                      OrderBy(output_names[0], "asc"),
                      OrderBy("Kaynak", "asc")],
            # Tavan dal sayisiyla carpiliyor: kullanici "ilk 10" derken 10
            # KIRILIM kastediyor, 10 satir degil. Iki kaynakli bir soruda
            # duz 10, her kaynaktan besini gostermek olurdu.
            limit=limit * len(branches),
        )

        try:
            sql = render_union(spec)
        except QuerySpecError as e:
            return self._error_result(str(e))

        logger.info(f"UNION SQL: {sql}")

        rows = self.data.read_sql(sql, params)

        self.audit["executedSql"] = sql
        self.audit["aggregationPerformedIn"] = "sql"
        self.audit["rowsRead"] = None
        self.audit["union"] = notes
        self.audit["resolvedGroupBy"] = output_names

        if rows.empty:
            return self._error_result(
                "Sorguya uyan kayıt bulunamadı. Filtreleri gevşetmeyi deneyin.")

        key_column = output_names[0]

        # Etiketler ilk gorulme sirasina gore: sorgu zaten olcuye gore sirali,
        # yani en buyuk kirilim basta.
        labels: List[str] = []
        for value in rows[key_column].tolist():
            text = self._label(value)
            if text not in labels:
                labels.append(text)

        datasets = []
        for branch in descriptors:
            source = branch["label"]
            by_label = {
                self._label(row[key_column]): row["value"]
                for _, row in rows[rows["Kaynak"] == source].iterrows()
            }
            # Eksik kirilim None kaliyor, sifir degil: sifir "bu kaynakta hic
            # yok" demektir, oysa gercek "ilk N'e giremedi" olabilir. Grafikte
            # bosluk gostermek, olmayan bir sifir cizmekten dogru.
            datasets.append({
                "label": source,
                "data": [self._number_or_none(by_label.get(name)) for name in labels],
            })

        return {
            "success": True,
            "charts": [{
                "type": chart_type,
                "title": title or f"{key_column} bazında {value_label}",
                "data": {"labels": labels, "datasets": datasets},
            }],
            "insights": [
                {"type": "info", "title": "Kaynaklar",
                 "description": " · ".join(d["label"] for d in descriptors)},
                {"type": "info", "title": "Kırılım Sayısı",
                 "description": f"{len(labels)} {key_column} değeri gösteriliyor"},
            ],
            "summary": desc or (
                f"{key_column} bazında {value_label}, "
                f"{len(descriptors)} kaynak karşılaştırıldı."),
        }

    @staticmethod
    def _short_table(name: str) -> str:
        """Sema onekini atip tablo adini birakir — etiket verilmediginde."""
        return str(name).split(".")[-1]

    @staticmethod
    def _number_or_none(value: Any) -> Optional[float]:
        """
        Grafige girecek sayi; okunamayan her sey None.

        NaN'i sifira cevirmek olmayan bir olcumu var gostermek olurdu; JSON'a
        oldugu gibi yazmak ise gecersiz JSON uretir.
        """
        if value is None:
            return None
        try:
            if pd.isna(value):
                return None
            return round(float(value), 2)
        except (TypeError, ValueError):
            return None

    @staticmethod
    def _label(value: Any) -> str:
        # Bos grup etiketi "None"/"NaT" diye gorunmesin: grafikte bunlar
        # kolon adi gibi okunuyor.
        try:
            if value is None or pd.isna(value):
                return "(Boş)"
        except (TypeError, ValueError):
            pass
        return str(value)

    def _load_table_data(self, base: TableRef, predicates: List[Predicate],
                         where_params: Dict, max_rows: int) -> pd.DataFrame:
        """
        Satir bazli veri yukler (model egitimi, korelasyon, ozet istatistik).

        Buradaki tavan kacinilmaz — ama artik gizlenmiyor: tavana degildiyse
        sonucun tablonun tamamini temsil etmedigi denetim izine ve kullaniciya
        yaziliyor.

        `unordered_sample` bilerek isaretli: TOP'un siralamasiz kullanildigi
        tek mesru durum bu. Isaretlenmeseydi dogrulama reddederdi — amac,
        belirsiz siralamanin bir daha kaza eseri olusmamasi.
        """
        spec = QuerySpec(
            base=base,
            where=predicates,
            limit=max_rows,
            select_all=True,
            unordered_sample=True,
        )
        query = render(spec)

        # Row analyses must not read columns outside the dictionary whitelist.
        from query_spec import quote_identifier
        projection = ", ".join(
            f"{quote_identifier(base.alias)}.{quote_identifier(column)}"
            for column in self._table_columns(base.schema, base.name).values())
        query = query.replace(f"{quote_identifier(base.alias)}.*", projection, 1)

        logger.info(f"SQL: {query}")

        df = self.data.read_sql(query, where_params, max_rows=max_rows)

        self.audit["executedSql"] = query
        self.audit["aggregationPerformedIn"] = "pandas"
        self.audit["rowsRead"] = len(df)
        self.audit["columnsRead"] = list(df.columns)
        self.audit["truncated"] = len(df) >= max_rows

        return df

    # Kimlik kolonlari olcum degildir: ortalamasi, korelasyonu ya da
    # standart sapmasi anlamli bir sey soylemez. Onceden bunlar da sayisal
    # sayildigi icin "istatistik" grafigi ReferenceId, CustomerCompanyId,
    # ShipperCompanyId gibi kolonlarin ortalamasini cizip duruyordu.
    _ID_SUFFIXES = ("id", "no", "kod", "code", "guid", "uuid")

    def _measure_columns(self, df: pd.DataFrame) -> List[str]:
        """Gercekten olculebilir sayisal kolonlar."""
        numeric = df.select_dtypes(include=["number"])
        keep = []
        for col in numeric.columns:
            lowered = col.lower()
            if any(lowered.endswith(s) for s in self._ID_SUFFIXES):
                continue
            # Her satirda farkli deger ureten sayisal kolon da pratikte bir
            # kimliktir (otomatik artan anahtarlar boyle goruniyor).
            if numeric[col].nunique(dropna=True) == len(numeric[col].dropna()) and len(numeric) > 1:
                continue
            keep.append(col)
        return keep

    def _statistics_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                             chart_type: str, title: str, desc: str) -> Dict:
        """
        Temel istatistik analizi.

        Yalnizca kullanici acikca ozet istatistik istediginde calisir. Onceden
        buraya sayma/gruplama sorulari da dusuyordu (LLM'in secebilecegi
        tiplerde `aggregation` yoktu) ve kullanici "en cok gidilen 5 ulke"
        diye sorup kimlik kolonlarinin ortalamasini goruyordu.
        """
        cols = self._measure_columns(df)[:15]

        if not cols:
            return self._error_result(
                "Ölçülebilir sayısal kolon bulunamadı — bu tabloda yalnızca "
                "kimlik ve metin kolonları var.")

        numeric_df = df[cols]
        means = [float(numeric_df[c].mean()) for c in cols]

        charts = [{
            "type": chart_type,
            "title": title or "Kolon Ortalamaları",
            "data": {
                "labels": cols,
                "datasets": [{"label": "Ortalama", "data": means}]
            }
        }]

        insights = [
            {"type": "info", "title": "Toplam Satır", "description": f"{len(df)} satır analiz edildi"},
            {"type": "info", "title": "Ölçülebilir Kolon", "description": f"{len(cols)} kolon değerlendirildi"},
        ]

        if target_col and target_col in numeric_df.columns:
            insights.append({
                "type": "success",
                "title": f"{target_col} İstatistikleri",
                "description": f"Ort: {numeric_df[target_col].mean():.2f}, "
                               f"Min: {numeric_df[target_col].min():.2f}, "
                               f"Max: {numeric_df[target_col].max():.2f}"
            })

        return {
            "success": True,
            "charts": charts,
            "insights": insights,
            "summary": desc or f"{len(df)} satır üzerinde istatistik analizi tamamlandı."
        }

    def _correlation_analysis(self, df: pd.DataFrame, title: str, desc: str) -> Dict:
        """
        Kolonlar arasi iliski.

        Ayri bir metot: onceden korelasyon grafigi her istatistik sonucuna
        kosulsuz ekleniyordu, yani kullanici "en cok gidilen 5 ulke" diye
        sordugunda cevaba hic istemedigi bir "En Yuksek Korelasyonlar"
        grafigi de dusuyordu.
        """
        cols = self._measure_columns(df)[:15]

        if len(cols) < 2:
            return self._error_result(
                "Korelasyon için en az iki ölçülebilir sayısal kolon gerekiyor.")

        corr = df[cols].corr()
        pairs = []
        for i in range(len(cols)):
            for j in range(i + 1, len(cols)):
                pairs.append({
                    "pair": f"{cols[i]} ↔ {cols[j]}",
                    "value": round(float(corr.iloc[i, j]), 3),
                })
        pairs.sort(key=lambda x: abs(x["value"]), reverse=True)
        top = pairs[:8]

        return {
            "success": True,
            "charts": [{
                "type": "bar",
                "title": title or "En Yüksek Korelasyonlar",
                "data": {
                    "labels": [p["pair"] for p in top],
                    "datasets": [{"label": "Korelasyon", "data": [p["value"] for p in top]}],
                },
            }],
            "insights": [
                {"type": "info", "title": "Karşılaştırılan Kolon",
                 "description": f"{len(cols)} ölçülebilir kolon arasında {len(pairs)} çift incelendi"},
            ],
            "summary": desc or f"{len(cols)} kolon arasındaki ilişkiler incelendi.",
        }

    def _regression_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                             features: List[str], chart_type: str, title: str, desc: str) -> Dict:
        """PyCaret regression analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if target_col not in numeric_df.columns:
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

        if len(numeric_df) < 30:
            return self._error_result("Regression için yeterli veri yok (min 30 satır)")

        try:
            from pycaret.regression import setup, compare_models, pull

            feature_cols = [c for c in features if c in numeric_df.columns and c != target_col]
            if not feature_cols:
                feature_cols = [c for c in numeric_df.columns if c != target_col][:10]

            use_df = numeric_df[feature_cols + [target_col]].copy()

            setup(data=use_df, target=target_col, session_id=42,
                  verbose=False, html=False, n_jobs=1)
            best = compare_models(n_select=1, sort="MAE", verbose=False)
            results_df = pull()

            model_names = results_df.index.tolist()[:5]
            mae_values = results_df["MAE"].head(5).tolist()
            r2_values = results_df["R2"].head(5).tolist()

            charts = [
                {
                    "type": "bar",
                    "title": title or f"{target_col} Tahmin Modeli Karşılaştırma (MAE)",
                    "data": {
                        "labels": model_names,
                        "datasets": [{"label": "MAE (Düşük = İyi)", "data": [round(v, 4) for v in mae_values]}]
                    }
                },
                {
                    "type": "bar",
                    "title": f"{target_col} Model R² Skorları",
                    "data": {
                        "labels": model_names,
                        "datasets": [{"label": "R² (Yüksek = İyi)", "data": [round(v, 4) for v in r2_values]}]
                    }
                }
            ]

            insights = [
                {"type": "success", "title": "En İyi Model", "description": f"{type(best).__name__}"},
                {"type": "info", "title": "Kullanılan Özellikler", "description": ", ".join(feature_cols[:5])},
                {"type": "info", "title": "Veri Boyutu", "description": f"{len(use_df)} satır, {len(feature_cols)} özellik"},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"{target_col} için regression analizi tamamlandı. En iyi model: {type(best).__name__}"}

        except Exception as e:
            logger.error(f"Regression hatası: {e}")
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

    def _classification_analysis(self, df: pd.DataFrame, target_col: Optional[str],
                                 features: List[str], chart_type: str, title: str, desc: str) -> Dict:
        """PyCaret classification analizi."""
        if not target_col or target_col not in df.columns:
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

        numeric_df = df.select_dtypes(include=["number"]).copy()
        if target_col not in numeric_df.columns:
            numeric_df[target_col] = df[target_col]

        numeric_df = numeric_df.dropna(subset=[target_col])

        if len(numeric_df) < 30:
            return self._error_result("Classification için yeterli veri yok")

        try:
            from pycaret.classification import setup, compare_models, pull

            feature_cols = [c for c in features if c in numeric_df.columns and c != target_col]
            if not feature_cols:
                feature_cols = [c for c in numeric_df.select_dtypes(include=["number"]).columns if c != target_col][:10]

            use_df = numeric_df[feature_cols + [target_col]].copy()

            setup(data=use_df, target=target_col, session_id=42,
                  verbose=False, html=False, n_jobs=1)
            best = compare_models(n_select=1, sort="Accuracy", verbose=False)
            results_df = pull()

            model_names = results_df.index.tolist()[:5]
            acc_values = results_df["Accuracy"].head(5).tolist()

            charts = [{
                "type": "bar",
                "title": title or f"{target_col} Classification Model Doğruluğu",
                "data": {
                    "labels": model_names,
                    "datasets": [{"label": "Accuracy", "data": [round(v, 4) for v in acc_values]}]
                }
            }]

            # Sınıf dağılımı
            value_counts = df[target_col].value_counts().head(10)
            charts.append({
                "type": "pie",
                "title": f"{target_col} Sınıf Dağılımı",
                "data": {
                    "labels": [str(x) for x in value_counts.index.tolist()],
                    "datasets": [{"label": "Adet", "data": value_counts.values.tolist()}]
                }
            })

            insights = [
                {"type": "success", "title": "En İyi Model", "description": type(best).__name__},
                {"type": "info", "title": "Sınıf Sayısı", "description": str(df[target_col].nunique())},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"{target_col} classification analizi tamamlandı."}

        except Exception as e:
            logger.error(f"Classification hatası: {e}")
            return self._statistics_analysis(df, target_col, chart_type, title, desc)

    def _anomaly_analysis(self, df: pd.DataFrame, features: List[str],
                          chart_type: str, title: str, desc: str) -> Dict:
        """Anomaly detection analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if len(numeric_df) < 30:
            return self._error_result("Anomaly detection için yeterli veri yok")

        try:
            from pycaret.anomaly import setup, create_model, assign_model

            cols = [c for c in features if c in numeric_df.columns][:10]
            if not cols:
                cols = numeric_df.columns.tolist()[:10]

            use_df = numeric_df[cols].copy()

            setup(data=use_df, session_id=42, verbose=False, html=False)
            model = create_model("iforest")
            results = assign_model(model)

            anomaly_count = int(results["Anomaly"].sum())
            normal_count = len(results) - anomaly_count

            charts = [{
                "type": "doughnut",
                "title": title or "Anomali Dağılımı",
                "data": {
                    "labels": ["Normal", "Anomali"],
                    "datasets": [{"label": "Adet", "data": [normal_count, anomaly_count]}]
                }
            }]

            # Anomaly score dağılımı
            scores = results["Anomaly_Score"].values
            import numpy as np
            hist, edges = np.histogram(scores, bins=10)
            charts.append({
                "type": "bar",
                "title": "Anomali Skor Dağılımı",
                "data": {
                    "labels": [f"{edges[i]:.2f}" for i in range(len(hist))],
                    "datasets": [{"label": "Frekans", "data": hist.tolist()}]
                }
            })

            insights = [
                {"type": "warning" if anomaly_count > 0 else "success",
                 "title": "Anomali Tespit",
                 "description": f"{anomaly_count} anomali tespit edildi ({anomaly_count / len(results) * 100:.1f}%)"},
                {"type": "info", "title": "Analiz Edilen Veri",
                 "description": f"{len(results)} satır, {len(cols)} özellik"},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"Anomaly detection tamamlandı. {anomaly_count} anomali bulundu."}

        except Exception as e:
            logger.error(f"Anomaly hatası: {e}")
            return self._error_result(str(e))

    def _clustering_analysis(self, df: pd.DataFrame, features: List[str],
                             chart_type: str, title: str, desc: str) -> Dict:
        """Clustering analizi."""
        numeric_df = df.select_dtypes(include=["number"]).dropna()

        if len(numeric_df) < 30:
            return self._error_result("Clustering için yeterli veri yok")

        try:
            from pycaret.clustering import setup, create_model, assign_model

            cols = [c for c in features if c in numeric_df.columns][:10]
            if not cols:
                cols = numeric_df.columns.tolist()[:10]

            use_df = numeric_df[cols].copy()

            setup(data=use_df, session_id=42, verbose=False, html=False)
            model = create_model("kmeans")
            results = assign_model(model)

            cluster_counts = results["Cluster"].value_counts().sort_index()

            charts = [{
                "type": "pie",
                "title": title or "Kümeleme Dağılımı",
                "data": {
                    "labels": [f"Küme {c}" for c in cluster_counts.index.tolist()],
                    "datasets": [{"label": "Adet", "data": cluster_counts.values.tolist()}]
                }
            }]

            # Küme merkezleri
            if len(cols) >= 2:
                charts.append({
                    "type": "bar",
                    "title": "Küme Ortalamaları (İlk 2 Özellik)",
                    "data": {
                        "labels": [f"Küme {c}" for c in cluster_counts.index.tolist()],
                        "datasets": [
                            {"label": cols[0], "data": [round(float(results[results["Cluster"] == c][cols[0]].mean()), 2) for c in cluster_counts.index]},
                            {"label": cols[1], "data": [round(float(results[results["Cluster"] == c][cols[1]].mean()), 2) for c in cluster_counts.index]},
                        ]
                    }
                })

            insights = [
                {"type": "success", "title": "Küme Sayısı", "description": f"{len(cluster_counts)} küme oluşturuldu"},
                {"type": "info", "title": "Kullanılan Özellikler", "description": ", ".join(cols[:5])},
            ]

            return {"success": True, "charts": charts, "insights": insights,
                    "summary": desc or f"Clustering tamamlandı. {len(cluster_counts)} küme bulundu."}

        except Exception as e:
            logger.error(f"Clustering hatası: {e}")
            return self._error_result(str(e))

    @staticmethod
    def _error_result(msg: str) -> Dict:
        # `error` alani sart: main.py sonucu `result.get("error")` ile okuyor
        # ve bu alan olmadigi icin butun basarisizliklar arayuze "Analysis
        # failed" diye gidiyordu. Sebebi bilen tek yer burasiyken kullanicinin
        # "kolon tabloda yok" gibi duzeltilebilir bir mesaji gormemesi icin
        # bir neden yok.
        return {
            "success": False,
            "error": msg,
            "charts": [],
            "insights": [{"type": "error", "title": "Hata", "description": msg}],
            "summary": f"Analiz başarısız: {msg}"
        }
