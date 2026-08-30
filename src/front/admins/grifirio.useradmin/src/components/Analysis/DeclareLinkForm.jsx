import React, { useEffect, useState } from 'react';
import { getTableColumns, learnFact } from '../../services/dataAnalysisService';

/**
 * İki kolonu elle eşleştirme formu.
 *
 * Yazım toleransı `Referance`/`Reference` gibi vakaları çözüyor ama sınıfı
 * çözmüyor: `F1`, `X_REF`, kısaltmalar — bu adlar hiçbir ipucu vermiyor ve
 * hiçbir çıkarım onları bulamaz. Veritabanını bilen insanın söylemesinden
 * başka yol yok.
 *
 * Kolonlar sözlükten değil GERÇEK ŞEMADAN okunuyor. Sözlük modelin
 * anlayabildiği kolonları yazıyor; elle bağlanması gereken kolonlar tam
 * olarak anlayamadıkları.
 *
 * Kayıt anında geçerli olmuyor: bir bağlantının işe yarayıp yaramadığı ancak
 * veritabanına bakılarak — hedef benzersiz mi, değerler örtüşüyor mu —
 * bilinebilir ve o ölçüm "Analiz Et" adımında yapılıyor. Form bunu açıkça
 * söylüyor; "kaydettim" deyip sonucun bir sonraki analize kalması, sessizce
 * çalışmayan bir düğmeden iyidir.
 */
export default function DeclareLinkForm({ connectionId, tables, onSaved }) {
  const [fromTable, setFromTable] = useState('');
  const [fromColumn, setFromColumn] = useState('');
  const [toTable, setToTable] = useState('');
  const [toColumn, setToColumn] = useState('');
  const [columns, setColumns] = useState({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(null);

  // Seçilen tablonun kolonları tembel yükleniyor: yirmi tablolu bir şemada
  // hepsini baştan çekmek gereksiz yirmi sorgu demek.
  useEffect(() => {
    let cancelled = false;
    const wanted = [fromTable, toTable].filter(t => t && !columns[t]);
    if (wanted.length === 0) return undefined;

    (async () => {
      for (const table of wanted) {
        try {
          const data = await getTableColumns(connectionId, table);
          if (cancelled) return;
          setColumns(prev => ({
            ...prev,
            [table]: (data.columns || []).map(c => c.columnName),
          }));
        } catch {
          if (!cancelled) setColumns(prev => ({ ...prev, [table]: [] }));
        }
      }
    })();

    return () => { cancelled = true; };
  }, [connectionId, fromTable, toTable, columns]);

  const complete = fromTable && fromColumn && toTable && toColumn;
  const sameTable = fromTable && fromTable === toTable;

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      await learnFact(connectionId, {
        kind: 'relationship',
        accepted: true,
        fromTable,
        fromColumn,
        toTable,
        toColumn,
      });
      setFromColumn('');
      setToColumn('');
      onSaved?.();
    } catch (e) {
      setError(e.response?.data?.error || e.message);
    } finally {
      setSaving(false);
    }
  };

  const columnSelect = (table, value, onChange, label) => (
    <label className="declare-link-field">
      <span>{label}</span>
      <select
        value={value}
        disabled={!table}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">{table ? 'Kolon seçin…' : 'Önce tablo seçin'}</option>
        {(columns[table] || []).map(c => <option key={c} value={c}>{c}</option>)}
      </select>
    </label>
  );

  return (
    <div className="declare-link">
      <p className="declare-link-hint">
        Adları birbirine benzemeyen kolonları buradan eşleştirebilirsiniz.
        Eşleştirme bir sonraki <strong>“Analiz Et”</strong>te ölçülür ve
        ondan sonra kullanılmaya başlar.
      </p>

      <div className="declare-link-row">
        <label className="declare-link-field">
          <span>Bu tablodaki</span>
          <select
            value={fromTable}
            onChange={(e) => { setFromTable(e.target.value); setFromColumn(''); }}
          >
            <option value="">Tablo seçin…</option>
            {tables.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
        {columnSelect(fromTable, fromColumn, setFromColumn, 'kolonu')}
      </div>

      <div className="declare-link-row">
        <label className="declare-link-field">
          <span>şu tablonun</span>
          <select
            value={toTable}
            onChange={(e) => { setToTable(e.target.value); setToColumn(''); }}
          >
            <option value="">Tablo seçin…</option>
            {tables.map(t => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
        {columnSelect(toTable, toColumn, setToColumn, 'kolonuna bağlanır')}
      </div>

      {/* Yön önemli ve kullanıcının bunu bilmesi gerekiyor: hedef taraf
          benzersiz olmak zorunda, yoksa join satırları çoğaltır. */}
      <p className="declare-link-note">
        İkinci tablo <strong>hedef</strong>: seçtiğiniz kolonun orada her
        değerden yalnızca bir satır olması gerekiyor (birincil anahtar ya da
        benzersiz kolon). Değilse eşleştirme ölçümde reddedilir ve sebebi
        listede yazar.
      </p>

      {sameTable && (
        <div className="declare-link-error">
          Bir tabloyu kendisine bağlayamazsınız.
        </div>
      )}
      {error && <div className="declare-link-error">{error}</div>}

      <button
        type="button"
        className="declare-link-save"
        disabled={!complete || sameTable || saving}
        onClick={save}
      >
        {saving ? 'Kaydediliyor…' : 'Bağlantıyı kaydet'}
      </button>
    </div>
  );
}
