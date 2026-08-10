"""
Veriye erisim kapisi.

Onceden analiz istegiyle birlikte musteri veritabaninin host/kullanici/sifre
bilgisi bu servise geliyor, SQLAlchemy motoru burada kuruluyordu. Iki sorunu
vardi:

  1. Sifre gereksiz yere ikinci bir servise, oradan da SQLAlchemy'nin hata
     metinlerine ve loglara yayiliyordu.
  2. Musteri veritabani firewall arkasinda oldugunda calismiyor. Cozum,
     baglantiyi musterinin aginda calisan bir bridge'in disari dogru kurmasi;
     boyle bir kurulumda sifre bulutta hic bulunmaz.

Artik bu servis SQL'i uretiyor, calistirmayi DataAnalysis.Api'ye birakiyor.
Veritabanina nasil ulasildigi (dogrudan mi, bridge uzerinden mi) burayi
ilgilendirmiyor.

`SqlAlchemyDataPort` yerel gelistirme icin duruyor: elde bir baglanti dizesi
varken tek servisle calisabilmek, hata ayiklamayi kolaylastiriyor.
"""

import logging
import os
import re
from typing import Any, Dict, Optional, Protocol

import pandas as pd

logger = logging.getLogger(__name__)


class DataPortError(RuntimeError):
    """Veri okunamadi. Cagiran taraf bunu kullaniciya gosterilecek hataya cevirir."""


class DataPort(Protocol):
    """Sorgu calistirip sonucu veren sey. Baglanti degil, sorgu konusur."""

    def read_sql(self, sql: str, params: Optional[Dict[str, Any]] = None,
                 max_rows: Optional[int] = None) -> pd.DataFrame:
        ...

    def scalar(self, sql: str, params: Optional[Dict[str, Any]] = None) -> Any:
        ...


class GatewayDataPort:
    """
    Sorguyu DataAnalysis.Api'nin ic ucuna gonderir.

    Kirpilma gizlenmiyor: sunucu tavana dayandigini soylerse `last_truncated`
    isaretleniyor ve cagiran taraf bunu denetim izine yaziyor. Tablonun
    tamamindan cikmis gibi duran eksik bir sonuc, en kotu sonuctur.
    """

    #: Servisler arasi paylasilan anahtarin gittigi baslik.
    API_KEY_HEADER = "X-Grafirio-Internal-Key"

    def __init__(self, connection_id: str, base_url: Optional[str] = None,
                 api_key: Optional[str] = None, timeout_seconds: int = 300):
        # requests, modulun kendisi degil burada iceri aliniyor: sahte bir
        # DataPort ile calisan testlerin bu bagimliligi kurmasi gerekmesin.
        import requests

        self._requests = requests
        self._connection_id = connection_id
        self._base_url = (
            base_url
            or os.getenv("DATA_ANALYSIS_API_URL")
            or "http://data-analysis-api:8080"
        ).rstrip("/")
        self._api_key = api_key or os.getenv("INTERNAL_API_KEY") or ""
        self._timeout = timeout_seconds

        if not self._api_key:
            raise DataPortError(
                "INTERNAL_API_KEY tanımlı değil; veri servisine bağlanılamaz.")

        self.last_truncated = False

    def read_sql(self, sql: str, params: Optional[Dict[str, Any]] = None,
                 max_rows: Optional[int] = None) -> pd.DataFrame:
        payload = self._post(sql, params, max_rows)

        self.last_truncated = bool(payload.get("truncated"))
        if self.last_truncated:
            logger.warning("Sonuç satır tavanına dayandı; veri kırpıldı.")

        rows = payload.get("rows") or []
        columns = payload.get("columns") or []

        # Bos sonucta kolon adlari bilinmiyor: sunucu satirlari okurken
        # ogrendigi icin hic satir yoksa listeyi bos donuyor. Cagiran taraflar
        # bos sonucu zaten ayrica ele aliyor (`frame.empty`), ama denetim izine
        # yazilan `columnsRead` bu durumda bos kaliyor.
        return pd.DataFrame(rows, columns=columns) if not rows else pd.DataFrame(rows)

    def scalar(self, sql: str, params: Optional[Dict[str, Any]] = None) -> Any:
        payload = self._post(sql, params, max_rows=1)
        rows = payload.get("rows") or []
        if not rows:
            return None

        first = rows[0]
        return next(iter(first.values()), None)

    @staticmethod
    def _to_tsql_placeholders(sql: str, params: Optional[Dict[str, Any]]) -> str:
        """
        `query_spec` SQLAlchemy tarzi `:isim` uretiyor, sorguyu calistiran
        Dapper ise `@isim` bekliyor.

        Cevirim yalnizca ELDEKI parametre adlari icin yapiliyor; sorgudaki her
        iki nokta ust ustenin degistirilmesi, '12:30:00' gibi metin
        degerlerini de bozardi.
        """
        if not params:
            return sql

        result = sql
        # Uzun ad once: ":musteri" ve ":musteri_id" birlikteyken kisa olanin
        # once degistirilmesi digerini yarim birakirdi.
        for name in sorted(params, key=len, reverse=True):
            result = re.sub(rf":{re.escape(name)}\b", f"@{name}", result)
        return result

    def _post(self, sql: str, params: Optional[Dict[str, Any]],
              max_rows: Optional[int]) -> Dict[str, Any]:
        body: Dict[str, Any] = {
            "connectionId": self._connection_id,
            "sql": self._to_tsql_placeholders(sql, params),
        }
        if params:
            body["parameters"] = params
        if max_rows:
            body["maxRows"] = max_rows

        try:
            response = self._requests.post(
                f"{self._base_url}/internal/data/query",
                json=body,
                headers={self.API_KEY_HEADER: self._api_key},
                timeout=self._timeout,
            )
        except Exception as exc:  # baglanti/timeout
            raise DataPortError(f"Veri servisine ulaşılamadı: {exc}") from exc

        if response.status_code != 200:
            # Govde sorgu hatasinin sebebini tasiyor; sifre icermez.
            raise DataPortError(
                f"Veri servisi hata döndü ({response.status_code}): {response.text[:500]}")

        return response.json()


class SqlAlchemyDataPort:
    """
    Dogrudan baglanti. Yalnizca yerel gelistirme ve hata ayiklama icin —
    uretimde veriye <see>GatewayDataPort</see> uzerinden gidiliyor.
    """

    def __init__(self, connection_string: str):
        import sqlalchemy

        self._sqlalchemy = sqlalchemy
        self.engine = sqlalchemy.create_engine(connection_string)
        self.last_truncated = False

    def read_sql(self, sql: str, params: Optional[Dict[str, Any]] = None,
                 max_rows: Optional[int] = None) -> pd.DataFrame:
        with self.engine.connect() as conn:
            frame = pd.read_sql(self._sqlalchemy.text(sql), conn, params=params or {})

        self.last_truncated = max_rows is not None and len(frame) > max_rows
        return frame.head(max_rows) if max_rows is not None else frame

    def scalar(self, sql: str, params: Optional[Dict[str, Any]] = None) -> Any:
        with self.engine.connect() as conn:
            return conn.execute(
                self._sqlalchemy.text(sql), params or {}).scalar()
