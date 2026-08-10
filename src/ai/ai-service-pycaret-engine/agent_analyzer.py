"""
PyCaret Agent Analyzer — LLM config'i ile dinamik analiz yapan modül.
MassTransit'ten gelen mesajları işler ve sonuçları geri gönderir.
"""

import json
import logging
import pandas as pd
from typing import Dict, Any, List, Optional

from data_port import DataPort
from query_spec import (
    AliasFactory, Aggregate, ColumnRef, OrderBy, Predicate, QuerySpec,
    QuerySpecError, TableRef, render, render_group_count,
)

logger = logging.getLogger(__name__)


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

    def run_analysis(self, config: Dict, params: Dict) -> Dict[str, Any]:
        """
        Config ve parametrelere göre analiz yap, grafik verileri ve insights döndür.
        """
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
            schema, table = self._split_table(target_table)
            columns = self._table_columns(schema, table)
        except Exception as e:
            logger.error(f"Tablo cozumlenemedi: {e}")
            return self._with_audit(self._error_result(str(e)))

        # Takma adlar burada dagitiliyor. Bugun tek tablo var, ama join'ler
        # geldiginde her kolon referansinin nitelenmis olmasi gerekecek; tek
        # tablo icin de ayni yoldan gecmek o gun icin surpriz birakmiyor.
        aliases = AliasFactory()
        base = TableRef(schema, table, aliases.take())

        # Filtreler kolon adlariyla dogrulanip parametreye baglaniyor.
        # Uygulanamayan filtre sessizce dusurulmuyor: "bu yil" diye sorup
        # tum zamanlarin sonucunu gormek, yanlis cevabin en sinsi hali.
        try:
            predicates, where_params, filter_notes = self._build_where(
                filters, columns, base.alias)
        except ValueError as e:
            return self._with_audit(self._error_result(str(e)))

        self.audit["appliedFilters"] = filter_notes

        try:
            # Gruplama/sayma sorulari SQL'de calisir: sonuc tablonun tamami
            # uzerinden ve kesindir. Onceden tablodan TOP 50000 satir cekilip
            # gruplama pandas'ta yapiliyordu — tablo bundan buyukse "en cok
            # gidilen 5 ulke" keyfi bir 50 binlik dilimin siralamasi oluyordu.
            if analysis_type == "aggregation":
                return self._with_audit(self._sql_aggregation(
                    base, columns, target_column, group_by, aggregation,
                    sort_order, limit, predicates, where_params, chart_type,
                    chart_title, description))

            # Kalan tipler (PyCaret modelleri, korelasyon, ozet istatistik)
            # satir bazli veri istiyor; bunlar icin okuma tavani kacinilmaz.
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
        cleaned = table_name.replace("[", "").replace("]", "").strip()
        parts = [p for p in cleaned.split(".") if p]
        if not parts:
            raise ValueError("Tablo adı boş.")
        return (parts[0], parts[-1]) if len(parts) > 1 else ("dbo", parts[0])

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
        frame = self.data.read_sql("""
            SELECT COLUMN_NAME
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = :schema AND TABLE_NAME = :table
            ORDER BY ORDINAL_POSITION
        """, {"schema": schema, "table": table})

        if frame.empty:
            raise ValueError(
                f"'{schema}.{table}' tablosu veritabanında bulunamadı.")

        names = frame.iloc[:, 0].tolist()
        return {str(name).lower(): str(name) for name in names}

    @staticmethod
    def _resolve_column(name: Optional[str], columns: Dict[str, str]) -> Optional[str]:
        if not name:
            return None
        return columns.get(str(name).replace("[", "").replace("]", "").strip().lower())

    @staticmethod
    def _column_missing(name: str, columns: Dict[str, str]) -> str:
        return (f"'{name}' kolonu tabloda yok. Mevcut kolonlar: "
                f"{', '.join(list(columns.values())[:20])}")

    # ------------------------------------------------------------------
    # Filtreler
    # ------------------------------------------------------------------

    _RANGE_OPS = {"gte": ">=", "gt": ">", "lte": "<=", "lt": "<", "eq": "=", "ne": "<>"}

    def _build_where(self, filters: Dict, columns: Dict[str, str],
                     table_alias: str) -> tuple:
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
            real = self._resolve_column(raw_col, columns)
            if real is None:
                raise ValueError(
                    f"Filtre uygulanamadı — {self._column_missing(str(raw_col), columns)}")

            column = ColumnRef(table_alias, real)

            if isinstance(value, dict):
                for op, operand in value.items():
                    sql_op = self._RANGE_OPS.get(str(op).lower())
                    if sql_op is None:
                        raise ValueError(
                            f"'{real}' filtresinde tanınmayan karşılaştırma: '{op}'.")
                    key = f"p{idx}"; idx += 1
                    predicates.append(Predicate(column, sql_op, [key]))
                    params[key] = operand
                    notes.append(f"{real} {sql_op} {operand}")

            elif isinstance(value, (list, tuple, set)):
                items = list(value)
                if not items:
                    raise ValueError(f"'{real}' için boş filtre listesi verildi.")
                keys = []
                for operand in items:
                    key = f"p{idx}"; idx += 1
                    keys.append(key)
                    params[key] = operand
                predicates.append(Predicate(column, "IN", keys))
                notes.append(f"{real} IN ({', '.join(str(i) for i in items)})")

            elif value is None:
                predicates.append(Predicate(column, "IS NULL"))
                notes.append(f"{real} boş")

            else:
                key = f"p{idx}"; idx += 1
                predicates.append(Predicate(column, "=", [key]))
                params[key] = value
                notes.append(f"{real} = {value}")

        return predicates, params, notes

    # ------------------------------------------------------------------
    # SQL gruplama
    # ------------------------------------------------------------------

    def _sql_aggregation(self, base: TableRef, columns: Dict[str, str],
                         target_col: Optional[str], group_by: List[str],
                         aggregation: str, sort_order: str, limit: int,
                         predicates: List[Predicate], where_params: Dict,
                         chart_type: str, title: str, desc: str) -> Dict:
        """
        Gruplama ve toplama — veritabaninda.

        Sonuc tablonun TAMAMI uzerinden hesaplanir. Bu metot pandas'taki
        eskisinin yerini aliyor: orada once TOP 50000 satir cekiliyor, gruplama
        o dilimde yapiliyordu. Tablo bundan buyukse "en cok gidilen 5 ulke"
        sorusunun cevabi, tablonun siralamasi bile garanti olmayan keyfi bir
        kesitinin siralamasi oluyordu — ve hicbir yerde bu yazmiyordu.
        """
        if not group_by:
            return self._error_result(
                "Sonucun hangi alana göre kırılacağı anlaşılamadı. Örneğin "
                "\"ülkeye göre\", \"müşteriye göre\" diye belirtebilirsiniz.")

        resolved_groups: List[str] = []
        for col in group_by:
            real = self._resolve_column(col, columns)
            if real is None:
                return self._error_result(
                    f"Gruplama yapılamadı — {self._column_missing(str(col), columns)}")
            resolved_groups.append(real)

        agg_key = str(aggregation or "count").strip().lower()
        real_target = self._resolve_column(target_col, columns)

        if target_col and real_target is None:
            return self._error_result(
                f"Hesaplanacak alan bulunamadı — {self._column_missing(str(target_col), columns)}")

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

        # Sonuc kolonunun adi tablodaki bir kolonla cakismamali; cakisirsa
        # pandas iki ayni adli kolon gorur ve grafige yanlis seri girer.
        alias = "value"
        while alias.lower() in columns:
            alias = "_" + alias

        group_refs = [ColumnRef(base.alias, c) for c in resolved_groups]
        direction = "asc" if str(sort_order).lower() == "asc" else "desc"

        # Siralama benzersiz olmali. "En cok gidilen 5 ulke" sorusunda iki ulke
        # esit sayidaysa hangisinin listeye girecegi yalnizca toplulastirmaya
        # gore siralandiginda belirsizdir — ayni soru iki farkli cevap verir.
        # Kirilim kolonlari ikincil siralama olarak ekleniyor.
        order: List[OrderBy] = [OrderBy(alias, direction)]
        order.extend(OrderBy(ref, "asc") for ref in group_refs)

        spec = QuerySpec(
            base=base,
            group_by=group_refs,
            aggregate=Aggregate(
                agg_key,
                ColumnRef(base.alias, real_target) if real_target else None,
                alias),
            where=predicates,
            order_by=order,
            limit=limit,
        )

        try:
            sql = render(spec)
            count_sql = render_group_count(spec)
        except QuerySpecError as e:
            return self._error_result(str(e))

        logger.info(f"SQL: {sql}")

        grouped = self.data.read_sql(sql, where_params)
        total_groups = self.data.scalar(count_sql, where_params)

        self.audit["executedSql"] = sql
        self.audit["aggregationPerformedIn"] = "sql"
        self.audit["rowsRead"] = None  # gruplama SQL'de: satir cekilmedi
        self.audit["groupCount"] = int(total_groups or 0)
        self.audit["resolvedGroupBy"] = resolved_groups
        self.audit["resolvedTargetColumn"] = real_target

        if grouped.empty:
            return self._error_result(
                "Sorguya uyan kayıt bulunamadı. Filtreleri gevşetmeyi deneyin.")

        labels = [self._label(v) for v in grouped[resolved_groups[0]].tolist()]
        values = [round(float(v), 2) for v in grouped[alias].fillna(0).tolist()]

        charts = [{
            "type": chart_type,
            "title": title or f"{resolved_groups[0]} bazında {value_label}",
            "data": {
                "labels": labels,
                "datasets": [{"label": value_label, "data": values}]
            }
        }]

        insights = [
            {"type": "info", "title": "Grup Sayısı",
             "description": f"{len(grouped)} grup gösteriliyor (toplam {total_groups})"},
            {"type": "success", "title": "En Yüksek" if direction == "DESC" else "En Düşük",
             "description": f"{labels[0]}: {values[0]}"},
        ]

        return {
            "success": True,
            "charts": charts,
            "insights": insights,
            "summary": desc or f"{resolved_groups[0]} bazında {value_label} hesaplandı.",
        }

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
