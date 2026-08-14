import { useCallback, useEffect, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_DOCUMENT_TYPES,
  createSubCompany,
  deleteCompanyDocument,
  describeError,
  documentTypeName,
  downloadCompanyDocument,
  fetchCompanies,
  fetchCompanyDocuments,
  updateCompany,
  uploadCompanyDocument,
} from '../../services/companyService';
import '../../styles/SettingsPages.css';

const COMPANY_TYPES = [
  { code: 'AS', name: 'Anonim Şirket (A.Ş.)' },
  { code: 'LTD', name: 'Limited Şirket (Ltd. Şti.)' },
  { code: 'SAHIS', name: 'Şahıs İşletmesi' },
  { code: 'KOLEKTIF', name: 'Kolektif Şirket' },
  { code: 'KOMANDIT', name: 'Komandit Şirket' },
  { code: 'KOOPERATIF', name: 'Kooperatif' },
  { code: 'DIGER', name: 'Diğer' },
];

const VKN_RE = /^\d{10}$/;
const IBAN_RE = /^TR\d{24}$/;

const toFormDate = (iso) => (iso ? String(iso).slice(0, 10) : '');

const companyToForm = (company) => ({
  name: company?.name ?? '',
  code: company?.code ?? '',
  description: company?.description ?? '',
  taxNumber: company?.taxNumber ?? '',
  taxOffice: company?.taxOffice ?? '',
  tradeRegistryNumber: company?.tradeRegistryNumber ?? '',
  mersisNumber: company?.mersisNumber ?? '',
  companyType: company?.companyType ?? '',
  establishmentDate: toFormDate(company?.establishmentDate),
  activityCode: company?.activityCode ?? '',
  activityDescription: company?.activityDescription ?? '',
  legalAddress: company?.legalAddress ?? '',
  kepAddress: company?.kepAddress ?? '',
  authorizedSignatoryName: company?.authorizedSignatoryName ?? '',
  authorizedSignatoryTitle: company?.authorizedSignatoryTitle ?? '',
  kvkkRepresentativeName: company?.kvkkRepresentativeName ?? '',
  kvkkRepresentativeEmail: company?.kvkkRepresentativeEmail ?? '',
  generalPhone: company?.generalPhone ?? '',
  generalEmail: company?.generalEmail ?? '',
  bankAccounts: (company?.bankAccounts ?? []).map((b) => ({
    bankName: b.bankName ?? '',
    branchName: b.branchName ?? '',
    iban: b.iban ?? '',
    accountHolder: b.accountHolder ?? '',
  })),
});

const EDITABLE_TABS = ['general', 'legal', 'contact', 'bank'];

/**
 * Ayarlar › Sirket Ayarlari.
 *
 * Kimlik, yasal/vergi, adres/iletisim ve banka alanlari tek bir form modeli
 * uzerinde tutuluyor ve tek bir PUT /companies/{id} cagrisiyla kaydediliyor
 * (backend'de hepsi ayni Company dokumaninin alanlari). Belgeler sekmesi
 * kendi CRUD'una sahip, ayri kaydediliyor. Alt sirketler sekmesi degismedi.
 */
export default function CompanySettingsPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [tab, setTab] = useState('general');
  const [companies, setCompanies] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [form, setForm] = useState(companyToForm(null));
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);

  const load = useCallback(async () => {
    if (!companyId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const list = await fetchCompanies(token);
      setCompanies(list);
      setForm(companyToForm(list.find((c) => c.id === companyId) || null));
      setDirty(false);
    } catch (err) {
      setError(describeError(err, 'Şirket bilgileri okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId]);

  useEffect(() => {
    load();
  }, [load]);

  const company = companies.find((c) => c.id === companyId) || null;
  const children = companies.filter((c) => c.parentCompanyId === companyId);

  const setField = (key) => (e) => {
    const { value } = e.target;
    setForm((f) => ({ ...f, [key]: value }));
    setDirty(true);
  };

  const setBankField = (index, key) => (e) => {
    const { value } = e.target;
    setForm((f) => {
      const next = f.bankAccounts.slice();
      next[index] = { ...next[index], [key]: value };
      return { ...f, bankAccounts: next };
    });
    setDirty(true);
  };

  const addBankAccount = () => {
    setForm((f) => ({
      ...f,
      bankAccounts: [...f.bankAccounts, { bankName: '', branchName: '', iban: '', accountHolder: '' }],
    }));
    setDirty(true);
  };

  const removeBankAccount = (index) => {
    setForm((f) => ({ ...f, bankAccounts: f.bankAccounts.filter((_, i) => i !== index) }));
    setDirty(true);
  };

  const discard = () => {
    setForm(companyToForm(company));
    setDirty(false);
    setError('');
  };

  const save = async () => {
    if (!form.name.trim()) {
      setError('Şirket adı gerekli.');
      return;
    }
    if (form.taxNumber && !VKN_RE.test(form.taxNumber.trim())) {
      setError('Vergi kimlik numarası 10 haneli olmalı.');
      return;
    }
    const invalidIban = form.bankAccounts.find(
      (b) => b.iban && !IBAN_RE.test(b.iban.replace(/\s/g, '').toUpperCase())
    );
    if (invalidIban) {
      setError('IBAN biçimi geçersiz — TR ile başlayan 26 karakter olmalı.');
      return;
    }

    setSaving(true);
    setError('');
    setNotice('');
    try {
      await updateCompany(token, companyId, {
        name: form.name.trim(),
        code: form.code.trim() || null,
        description: form.description.trim() || null,
        taxNumber: form.taxNumber.trim() || null,
        taxOffice: form.taxOffice.trim() || null,
        tradeRegistryNumber: form.tradeRegistryNumber.trim() || null,
        mersisNumber: form.mersisNumber.trim() || null,
        companyType: form.companyType || null,
        establishmentDate: form.establishmentDate || null,
        activityCode: form.activityCode.trim() || null,
        activityDescription: form.activityDescription.trim() || null,
        legalAddress: form.legalAddress.trim() || null,
        kepAddress: form.kepAddress.trim() || null,
        authorizedSignatoryName: form.authorizedSignatoryName.trim() || null,
        authorizedSignatoryTitle: form.authorizedSignatoryTitle.trim() || null,
        kvkkRepresentativeName: form.kvkkRepresentativeName.trim() || null,
        kvkkRepresentativeEmail: form.kvkkRepresentativeEmail.trim() || null,
        generalPhone: form.generalPhone.trim() || null,
        generalEmail: form.generalEmail.trim() || null,
        bankAccounts: form.bankAccounts.filter((b) => b.bankName.trim() || b.iban.trim()),
      });
      setNotice('Şirket bilgileri kaydedildi.');
      setDirty(false);
      await load();
    } catch (err) {
      setError(describeError(err, 'Kaydedilemedi.'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Şirket</p>
          <h1>Şirket Ayarları</h1>
          <p className="st-lead">
            Kurum kimliği, yasal/vergi bilgileri, resmi iletişim, banka hesapları, belgeler ve alt
            şirket hiyerarşisi.
          </p>
        </div>
      </div>

      {!loading && companyId && (
        <div className="st-tabs" role="tablist">
          <button type="button" className={tab === 'general' ? 'is-active' : ''} onClick={() => setTab('general')}>
            Genel
          </button>
          <button type="button" className={tab === 'legal' ? 'is-active' : ''} onClick={() => setTab('legal')}>
            Yasal & Vergi
          </button>
          <button type="button" className={tab === 'contact' ? 'is-active' : ''} onClick={() => setTab('contact')}>
            Adres & İletişim
          </button>
          <button type="button" className={tab === 'bank' ? 'is-active' : ''} onClick={() => setTab('bank')}>
            Banka
          </button>
          <button type="button" className={tab === 'documents' ? 'is-active' : ''} onClick={() => setTab('documents')}>
            Belgeler
          </button>
          <button type="button" className={tab === 'tree' ? 'is-active' : ''} onClick={() => setTab('tree')}>
            Alt şirketler {children.length > 0 && `(${children.length})`}
          </button>
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && !companyId && (
        <div className="st-card">
          <p className="st-empty">
            Hesabınız henüz bir şirkete bağlı değil. Çalışma alanınızı kurduktan sonra bu sayfa
            şirketinizin ayarlarını gösterir.
          </p>
        </div>
      )}

      {!loading && companyId && tab === 'general' && (
        <div className="st-grid-2">
          <section className="st-card">
            <p className="st-caps">Kimlik</p>

            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr' }}>
              <label className="st-field">
                <span>Şirket adı</span>
                <input value={form.name} onChange={setField('name')} placeholder="Akdeniz Tekstil A.Ş." />
              </label>
            </div>

            <div className="st-form-grid" style={{ marginTop: 14 }}>
              <label className="st-field st-field--mono">
                <span>Kısa kod</span>
                <input value={form.code} onChange={setField('code')} placeholder="AKD" />
              </label>
              <label className="st-field st-field--mono">
                <span>Hiyerarşi seviyesi</span>
                <input value={company?.level ?? 0} readOnly disabled />
              </label>
            </div>

            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr', marginTop: 14 }}>
              <label className="st-field">
                <span>Açıklama</span>
                <textarea rows="3" value={form.description} onChange={setField('description')} />
              </label>
            </div>
          </section>

          <div className="st-col">
            <section className="st-card">
              <p className="st-caps">Bölgesel biçimler</p>
              <div className="st-form-grid">
                <label className="st-field">
                  <span>Dil</span>
                  <select disabled>
                    <option>Türkçe</option>
                  </select>
                </label>
                <label className="st-field">
                  <span>Saat dilimi</span>
                  <select disabled>
                    <option>(UTC+03:00) İstanbul</option>
                  </select>
                </label>
                <label className="st-field">
                  <span>Para birimi</span>
                  <select disabled>
                    <option>₺ Türk lirası</option>
                  </select>
                </label>
                <label className="st-field">
                  <span>Tarih biçimi</span>
                  <select disabled>
                    <option>GG.AA.YYYY</option>
                  </select>
                </label>
              </div>
              <p className="st-card-sub" style={{ marginTop: 14 }}>
                Panel şu an bu biçimleri sabit kullanıyor; seçim yapılabilmesi için tercihleri
                saklayacak bir uç gerekiyor.
              </p>
            </section>
          </div>
        </div>
      )}

      {!loading && companyId && tab === 'legal' && (
        <section className="st-card">
          <p className="st-caps">Yasal ve vergi kimliği</p>
          <div className="st-form-grid">
            <label className="st-field st-field--mono">
              <span>Vergi kimlik numarası (VKN)</span>
              <input value={form.taxNumber} onChange={setField('taxNumber')} placeholder="1234567890" maxLength={10} />
            </label>
            <label className="st-field">
              <span>Vergi dairesi</span>
              <input value={form.taxOffice} onChange={setField('taxOffice')} placeholder="Kadıköy V.D." />
            </label>
            <label className="st-field st-field--mono">
              <span>Ticaret sicil no</span>
              <input value={form.tradeRegistryNumber} onChange={setField('tradeRegistryNumber')} />
            </label>
            <label className="st-field st-field--mono">
              <span>MERSİS no</span>
              <input value={form.mersisNumber} onChange={setField('mersisNumber')} />
            </label>
            <label className="st-field">
              <span>Şirket türü</span>
              <select value={form.companyType} onChange={setField('companyType')}>
                <option value="">Seçin…</option>
                {COMPANY_TYPES.map((t) => (
                  <option key={t.code} value={t.code}>
                    {t.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="st-field">
              <span>Kuruluş tarihi</span>
              <input type="date" value={form.establishmentDate} onChange={setField('establishmentDate')} />
            </label>
            <label className="st-field st-field--mono">
              <span>Faaliyet kodu</span>
              <input value={form.activityCode} onChange={setField('activityCode')} placeholder="NACE kodu" />
            </label>
          </div>
          <div className="st-form-grid" style={{ gridTemplateColumns: '1fr', marginTop: 14 }}>
            <label className="st-field">
              <span>Faaliyet açıklaması</span>
              <textarea rows="2" value={form.activityDescription} onChange={setField('activityDescription')} />
            </label>
          </div>
        </section>
      )}

      {!loading && companyId && tab === 'contact' && (
        <section className="st-card">
          <p className="st-caps">Adres ve resmi iletişim</p>
          <div className="st-form-grid" style={{ gridTemplateColumns: '1fr' }}>
            <label className="st-field">
              <span>Tebligat adresi</span>
              <textarea rows="2" value={form.legalAddress} onChange={setField('legalAddress')} />
            </label>
          </div>
          <div className="st-form-grid" style={{ marginTop: 14 }}>
            <label className="st-field st-field--mono">
              <span>KEP adresi</span>
              <input value={form.kepAddress} onChange={setField('kepAddress')} placeholder="firma@hs01.kep.tr" />
            </label>
            <label className="st-field">
              <span>Genel telefon</span>
              <input value={form.generalPhone} onChange={setField('generalPhone')} />
            </label>
            <label className="st-field">
              <span>Genel e-posta</span>
              <input value={form.generalEmail} onChange={setField('generalEmail')} />
            </label>
            <label className="st-field">
              <span>Yetkili imza sahibi</span>
              <input value={form.authorizedSignatoryName} onChange={setField('authorizedSignatoryName')} />
            </label>
            <label className="st-field">
              <span>Yetkilinin unvanı</span>
              <input value={form.authorizedSignatoryTitle} onChange={setField('authorizedSignatoryTitle')} />
            </label>
            <label className="st-field">
              <span>KVKK veri sorumlusu temsilcisi</span>
              <input value={form.kvkkRepresentativeName} onChange={setField('kvkkRepresentativeName')} />
            </label>
            <label className="st-field">
              <span>KVKK temsilcisi e-posta</span>
              <input value={form.kvkkRepresentativeEmail} onChange={setField('kvkkRepresentativeEmail')} />
            </label>
          </div>
        </section>
      )}

      {!loading && companyId && tab === 'bank' && (
        <section className="st-card">
          <div className="st-card-head">
            <div>
              <h2>Banka hesapları</h2>
              <p className="st-card-sub">Faturada gösterilecek hesaplar.</p>
            </div>
            <button type="button" className="st-btn st-btn--sm" onClick={addBankAccount}>
              + Hesap ekle
            </button>
          </div>

          {form.bankAccounts.length === 0 ? (
            <p className="st-empty">Henüz banka hesabı eklenmedi.</p>
          ) : (
            form.bankAccounts.map((b, i) => (
              <div
                key={i}
                className="st-form-grid"
                style={{ marginBottom: 14, paddingBottom: 14, borderBottom: '1px solid var(--gf-line-soft)' }}
              >
                <label className="st-field">
                  <span>Banka</span>
                  <input value={b.bankName} onChange={setBankField(i, 'bankName')} />
                </label>
                <label className="st-field">
                  <span>Şube</span>
                  <input value={b.branchName} onChange={setBankField(i, 'branchName')} />
                </label>
                <label className="st-field st-field--mono">
                  <span>IBAN</span>
                  <input value={b.iban} onChange={setBankField(i, 'iban')} placeholder="TR…" />
                </label>
                <label className="st-field">
                  <span>Hesap sahibi</span>
                  <input value={b.accountHolder} onChange={setBankField(i, 'accountHolder')} />
                </label>
                <button
                  type="button"
                  className="st-link"
                  style={{ alignSelf: 'flex-end', marginBottom: 11 }}
                  onClick={() => removeBankAccount(i)}
                >
                  Kaldır
                </button>
              </div>
            ))
          )}
        </section>
      )}

      {!loading && companyId && EDITABLE_TABS.includes(tab) && (
        <div className="st-savebar">
          <span className="st-savebar-note">{dirty ? 'kaydedilmemiş değişiklikler var' : 'güncel'}</span>
          <span className="st-savebar-actions">
            <button type="button" className="st-btn st-btn--ghost" disabled={!dirty || saving} onClick={discard}>
              Vazgeç
            </button>
            <button type="button" className="st-btn" disabled={saving} onClick={save}>
              {saving ? 'Kaydediliyor…' : 'Kaydet'}
            </button>
          </span>
        </div>
      )}

      {!loading && companyId && tab === 'documents' && (
        <CompanyDocumentsPanel token={token} companyId={companyId} setError={setError} setNotice={setNotice} />
      )}

      {!loading && companyId && tab === 'tree' && (
        <SubCompaniesPanel
          token={token}
          parentId={companyId}
          children={children}
          onCreated={load}
          setError={setError}
          setNotice={setNotice}
        />
      )}
    </div>
  );
}

function CompanyDocumentsPanel({ token, companyId, setError, setNotice }) {
  const [documents, setDocuments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [file, setFile] = useState(null);
  const [form, setForm] = useState({ documentType: COMPANY_DOCUMENT_TYPES[0].code, expiryDate: '', note: '' });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await fetchCompanyDocuments(token, companyId);
      setDocuments(list);
    } catch (err) {
      setError(describeError(err, 'Belgeler okunamadı.'));
    } finally {
      setLoading(false);
    }
  }, [token, companyId, setError]);

  useEffect(() => {
    load();
  }, [load]);

  const submit = async (e) => {
    e.preventDefault();
    if (!file) {
      setError('Yüklenecek bir dosya seçin.');
      return;
    }
    setUploading(true);
    setError('');
    setNotice('');
    try {
      await uploadCompanyDocument(token, companyId, { file, ...form });
      setFile(null);
      setForm({ documentType: COMPANY_DOCUMENT_TYPES[0].code, expiryDate: '', note: '' });
      setNotice('Belge yüklendi.');
      await load();
    } catch (err) {
      setError(describeError(err, 'Belge yüklenemedi.'));
    } finally {
      setUploading(false);
    }
  };

  const remove = async (doc) => {
    setError('');
    setNotice('');
    try {
      await deleteCompanyDocument(token, companyId, doc.id);
      setNotice('Belge silindi.');
      await load();
    } catch (err) {
      setError(describeError(err, 'Belge silinemedi.'));
    }
  };

  const download = async (doc) => {
    setError('');
    try {
      await downloadCompanyDocument(token, companyId, doc.id, doc.originalFileName);
    } catch (err) {
      setError(describeError(err, 'Belge indirilemedi.'));
    }
  };

  const fmtDate = (str) =>
    str ? new Date(str).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';

  const expiryBadge = (doc) => {
    if (!doc.expiryDate) return null;
    const days = (new Date(doc.expiryDate) - new Date()) / 86400000;
    if (days < 0) return <span className="st-badge st-badge--err">Süresi doldu</span>;
    if (days < 30) return <span className="st-badge st-badge--warn">Yakında dolacak</span>;
    return <span className="st-badge st-badge--ok">Geçerli</span>;
  };

  return (
    <section className="st-card">
      <p className="st-caps">Belgeler</p>

      {loading && <p className="st-empty">Yükleniyor…</p>}

      {!loading && documents.length === 0 && <p className="st-empty">Henüz belge yüklenmedi.</p>}

      {!loading && documents.length > 0 && (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Belge</th>
                <th>Tür</th>
                <th>Geçerlilik</th>
                <th>Yüklenme</th>
                <th className="st-right">İşlem</th>
              </tr>
            </thead>
            <tbody>
              {documents.map((doc) => (
                <tr key={doc.id}>
                  <td className="st-strong">{doc.originalFileName}</td>
                  <td>{documentTypeName(doc.documentType)}</td>
                  <td>
                    {doc.expiryDate ? fmtDate(doc.expiryDate) : '—'} {expiryBadge(doc)}
                  </td>
                  <td className="st-mono st-dim">{fmtDate(doc.uploadedAt)}</td>
                  <td className="st-right">
                    <button type="button" className="st-link" onClick={() => download(doc)}>
                      İndir
                    </button>{' '}
                    <button type="button" className="st-link" onClick={() => remove(doc)}>
                      Sil
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={submit} style={{ marginTop: 20, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <p className="st-caps" style={{ margin: 0 }}>
          Belge yükle
        </p>
        <div className="st-form-grid">
          <label className="st-field">
            <span>Belge türü</span>
            <select value={form.documentType} onChange={(e) => setForm({ ...form, documentType: e.target.value })}>
              {COMPANY_DOCUMENT_TYPES.map((t) => (
                <option key={t.code} value={t.code}>
                  {t.name}
                </option>
              ))}
            </select>
          </label>
          <label className="st-field">
            <span>Geçerlilik tarihi (opsiyonel)</span>
            <input
              type="date"
              value={form.expiryDate}
              onChange={(e) => setForm({ ...form, expiryDate: e.target.value })}
            />
          </label>
          <label className="st-field" style={{ gridColumn: '1 / -1' }}>
            <span>Not (opsiyonel)</span>
            <input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} />
          </label>
          <label className="st-field">
            <span>Dosya</span>
            <input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
          </label>
        </div>
        <button type="submit" className="st-btn" disabled={uploading} style={{ alignSelf: 'flex-start' }}>
          {uploading ? 'Yükleniyor…' : '+ Belge yükle'}
        </button>
      </form>
    </section>
  );
}

function SubCompaniesPanel({ token, parentId, children, onCreated, setError, setNotice }) {
  const [form, setForm] = useState({ name: '', code: '' });
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setError('Alt şirket adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    setNotice('');
    try {
      await createSubCompany(token, {
        name: form.name.trim(),
        code: form.code.trim(),
        parentCompanyId: parentId,
      });
      setForm({ name: '', code: '' });
      setNotice('Alt şirket oluşturuldu.');
      await onCreated();
    } catch (err) {
      setError(describeError(err, 'Alt şirket oluşturulamadı.'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="st-card">
      {children.length === 0 ? (
        <p className="st-empty">Henüz alt şirket yok.</p>
      ) : (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Ad</th>
                <th>Kısa kod</th>
                <th>Seviye</th>
              </tr>
            </thead>
            <tbody>
              {children.map((c) => (
                <tr key={c.id}>
                  <td className="st-strong">{c.name}</td>
                  <td className="st-mono st-dim">{c.code || '—'}</td>
                  <td className="st-mono st-dim">{c.level}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={submit} style={{ marginTop: 20, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <p className="st-caps" style={{ margin: 0 }}>Alt şirket ekle</p>
        <div className="st-form-grid">
          <label className="st-field">
            <span>Ad</span>
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Akdeniz Tekstil Lojistik"
            />
          </label>
          <label className="st-field st-field--mono">
            <span>Kısa kod</span>
            <input
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value })}
              placeholder="AKD-LOJ"
            />
          </label>
        </div>
        <button type="submit" className="st-btn" disabled={busy} style={{ alignSelf: 'flex-start' }}>
          {busy ? 'Ekleniyor…' : '+ Alt şirket ekle'}
        </button>
      </form>
    </section>
  );
}
