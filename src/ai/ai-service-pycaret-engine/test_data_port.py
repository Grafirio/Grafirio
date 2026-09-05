"""
Veri kapisinin degismezleri.

Calistirma (bagimlilik gerekmez, pytest de gerekmez):

    python test_data_port.py

Burada korunan iki sey var:

  * Parametre yer tutucularinin cevrimi. `query_spec` SQLAlchemy tarzi `:isim`
    uretiyor, sorguyu calistiran Dapper `@isim` bekliyor. Cevrim yalnizca
    eldeki parametre adlarina uygulanmali — sorgudaki her iki nokta ust ustenin
    degistirilmesi '12:30:00' gibi metin degerlerini bozardi.
  * Filtre degerleri SQL metnine girmez; govdede ayri alanda gider.

Ikisi de kirildiginda sorgu calismaya devam eder, yalnizca yanlis calisir.
"""

import sys
import types
from unittest.mock import patch

FAILS = []


def check(name, got, want):
    if got != want:
        FAILS.append(f"{name}\n  beklenen: {want}\n  gelen   : {got}")


# pandas sahteleniyor: bu dosyada olculen sey istek govdesinin nasil
# kuruldugu, DataFrame'in nasil olustugu degil.
_pandas = types.ModuleType("pandas")
_pandas.DataFrame = lambda rows, columns=None: {"rows": rows, "columns": columns}

# `requests` de sahteleniyor: kurucu onu iceri aliyor, ama her ornekte
# asagidaki sahte istemciyle degistiriliyor. Dosyanin bagimliliksiz
# calisabilmesi icin.
with patch.dict(sys.modules, {"pandas": _pandas}):
    from data_port import GatewayDataPort


class _FakeResponse:
    status_code = 200
    text = ""

    def __init__(self, payload):
        self._payload = payload

    def json(self):
        return self._payload


class _FakeRequests:
    """Son gonderilen istegi tutan sahte `requests`."""

    def __init__(self, payload=None):
        self.last = None
        self._payload = payload or {"columns": [], "rows": [], "truncated": False}

    def post(self, url, json=None, headers=None, timeout=None):
        self.last = {"url": url, "json": json, "headers": headers}
        return _FakeResponse(self._payload)


def port(payload=None):
    p = GatewayDataPort("11111111-2222-3333-4444-555555555555",
                        "company", "query", "config", "a" * 64,
                        base_url="http://api", api_key="anahtar")
    p._requests = _FakeRequests(payload)
    return p


# ── Yer tutucu cevrimi ────────────────────────────────────────────────────

translate = GatewayDataPort._to_tsql_placeholders

check("parametre adi cevriliyor",
      translate("WHERE [Ulke] = :p0", {"p0": "TR"}),
      "WHERE [Ulke] = @p0")

check("birden fazla parametre",
      translate("WHERE a = :p0 AND b IN (:p1, :p2)", {"p0": 1, "p1": 2, "p2": 3}),
      "WHERE a = @p0 AND b IN (@p1, @p2)")

# Kisa ad once degistirilseydi ":p10" -> "@p1" + "0" olurdu.
check("uzun ad once eslesir",
      translate("WHERE a = :p1 AND b = :p10", {"p1": 1, "p10": 2}),
      "WHERE a = @p1 AND b = @p10")

# Asil koruma: parametre olmayan iki nokta ust uste dokunulmadan kalmali.
check("saat degeri bozulmuyor",
      translate("WHERE t > '12:30:00' AND a = :p0", {"p0": 1}),
      "WHERE t > '12:30:00' AND a = @p0")

check("parametre yoksa sorgu aynen kalir",
      translate("SELECT 1 WHERE t = '00:00'", None),
      "SELECT 1 WHERE t = '00:00'")


# ── Istek govdesi ─────────────────────────────────────────────────────────

p = port()
p.read_sql("SELECT TOP 10 * FROM [dbo].[Shipments] WHERE [Ulke] = :p0", {"p0": "TR"})
sent = p._requests.last

check("uc adresi", sent["url"], "http://api/internal/data/query")
check("anahtar basligi", sent["headers"][GatewayDataPort.API_KEY_HEADER], "anahtar")
for key, value in {"companyId": "company", "queryId": "query", "configId": "config", "configHash": "a" * 64}.items():
    check(f"required context {key}", sent["json"][key], value)
check("sorgu cevrilmis halde gidiyor",
      sent["json"]["sql"],
      "SELECT TOP 10 * FROM [dbo].[Shipments] WHERE [Ulke] = @p0")
check("deger govdede ayri alanda", sent["json"]["parameters"], {"p0": "TR"})
check("deger SQL metnine girmiyor", "TR" in sent["json"]["sql"], False)


# ── Kirpilma gizlenmiyor ──────────────────────────────────────────────────

p = port({"columns": ["a"], "rows": [{"a": 1}], "truncated": True})
p.read_sql("SELECT a FROM t")
check("kirpilma isaretleniyor", p.last_truncated, True)

p = port({"columns": ["a"], "rows": [{"a": 1}], "truncated": False})
p.read_sql("SELECT a FROM t")
check("kirpilmadiysa isaretlenmiyor", p.last_truncated, False)


# ── scalar ────────────────────────────────────────────────────────────────

p = port({"columns": ["Toplam"], "rows": [{"Toplam": 42}], "truncated": False})
check("scalar ilk hucreyi doner", p.scalar("SELECT COUNT(*) AS Toplam FROM t"), 42)

p = port({"columns": [], "rows": [], "truncated": False})
check("bos sonucta scalar None", p.scalar("SELECT COUNT(*) FROM t"), None)


if FAILS:
    print(f"\n{len(FAILS)} kontrol başarısız:\n")
    for failure in FAILS:
        print(f"  ✗ {failure}\n")
    sys.exit(1)

print("Tüm kontroller geçti.")
