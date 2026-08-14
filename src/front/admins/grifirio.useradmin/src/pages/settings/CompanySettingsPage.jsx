import { useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import {
  COMPANY_DOCUMENT_TYPES,
  createSubCompany,
  deleteCompanyDocument,
  describeError,
  documentTypeName,
  downloadCompanyDocument,
  fetchCompanies,
  fetchCompanyDocumentThumbnail,
  fetchCompanyDocuments,
  fetchCurrentCompany,
  updateCompany,
  uploadCompanyDocument,
} from '../../services/companyService';
import {
  COUNTRIES,
  CURRENCIES,
  IBAN_RE,
  INDUSTRY_SCHEMES,
  LOCALES,
  TIME_ZONES,
  countryProfile,
} from '../../constants/countryProfiles';
import '../../styles/SettingsPages.css';

const TABS = [
  { key: 'general', label: 'Genel' },
  { key: 'legal', label: 'Yasal Kimlik' },
  { key: 'contact', label: 'İletişim' },
  { key: 'addresses', label: 'Adresler' },
  { key: 'bank', label: 'Banka' },
  { key: 'documents', label: 'Belgeler' },
  { key: 'tree', label: 'Alt şirketler' },
];

/** Kaydetme çubuğu yalnızca form taşıyan sekmelerde görünür. */
const FORM_TABS = ['general', 'legal', 'contact', 'addresses', 'bank'];

const ADDRESS_TYPES = [
  { code: 'REGISTERED', name: 'Kayıtlı adres (tebligat)' },
  { code: 'BILLING', name: 'Fatura adresi' },
  { code: 'OPERATIONAL', name: 'Operasyon adresi' },
  { code: 'SHIPPING', name: 'Sevkiyat adresi' },
];

const MONTHS = [
  'Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran',
  'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık',
];

const toFormDate = (iso) => (iso ? String(iso).slice(0, 10) : '');

const emptyAddress = () => ({
  type: 'REGISTERED',
  line1: '',
  line2: '',
  city: '',
  region: '',
  postalCode: '',
  countryCode: '',
  isPrimary: false,
});

const emptyBankAccount = () => ({
  bankName: '',
  branchName: '',
  accountHolder: '',
  countryCode: '',
  currency: '',
  iban: '',
  accountNumber: '',
  routingCode: '',
  swiftBic: '',
  isPrimary: false,
});

const companyToForm = (c) => ({
  name: c?.name ?? '',
  legalName: c?.legalName ?? '',
  code: c?.code ?? '',
  countryCode: c?.countryCode ?? '',
  legalForm: c?.legalForm ?? '',
  registrationNumber: c?.registrationNumber ?? '',
  taxId: c?.taxId ?? '',
  incorporationDate: toFormDate(c?.incorporationDate),
  taxOffice: c?.taxOffice ?? '',
  secondaryRegistrationNumber: c?.secondaryRegistrationNumber ?? '',
  leiCode: c?.leiCode ?? '',
  dunsNumber: c?.dunsNumber ?? '',
  industryScheme: c?.industryScheme ?? '',
  industryCode: c?.industryCode ?? '',
  industryDescription: c?.industryDescription ?? '',
  description: c?.description ?? '',
  baseCurrency: c?.baseCurrency ?? '',
  locale: c?.locale ?? '',
  timeZoneId: c?.timeZoneId ?? '',
  fiscalYearStartMonth: c?.fiscalYearStartMonth ? String(c.fiscalYearStartMonth) : '',
  teamSize: c?.teamSize ?? '',
  generalPhone: c?.generalPhone ?? '',
  generalEmail: c?.generalEmail ?? '',
  website: c?.website ?? '',
  eInvoiceScheme: c?.eInvoiceScheme ?? '',
  eInvoiceAddress: c?.eInvoiceAddress ?? '',
  authorizedSignatoryName: c?.authorizedSignatoryName ?? '',
  authorizedSignatoryTitle: c?.authorizedSignatoryTitle ?? '',
  dataProtectionOfficerName: c?.dataProtectionOfficerName ?? '',
  dataProtectionOfficerEmail: c?.dataProtectionOfficerEmail ?? '',
  privacyRepresentativeName: c?.privacyRepresentativeName ?? '',
  privacyRepresentativeEmail: c?.privacyRepresentativeEmail ?? '',
  privacyRepresentativeCountryCode: c?.privacyRepresentativeCountryCode ?? '',
  addresses: (c?.addresses ?? []).map((a) => ({ ...emptyAddress(), ...a })),
  bankAccounts: (c?.bankAccounts ?? []).map((b) => ({ ...emptyBankAccount(), ...b })),
});

/**
 * Ayarlar › Şirket Ayarları.
 *
 * Şirket bilgisi /companies/current ucundan geliyor; önceki hali token'daki
 * company_id ve accessible_companies claim'lerine bağlıydı ve biri eksik
 * olduğunda kullanıcı kayıt sırasında kendi kurduğu şirketi bile göremiyordu.
 *
 * Kimlik alanları (yasal ad, kısa kod, ülke, tüzel yapı, sicil ve vergi no)
 * bir kez dolduktan sonra kilitleniyor. Buradaki kilit yalnızca görsel; asıl
 * kural sunucuda (UpdateCompanyCommandHandler), çünkü salt-okunur bir input
 * isteğin doğrudan gönderilmesini engellemez.
 */
export default function CompanySettingsPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;

  const [searchParams, setSearchParams] = useSearchParams();
  const tab = TABS.some((t) => t.key === searchParams.get('tab'))
    ? searchParams.get('tab')
    : 'general';

  const setTab = (key) => setSearchParams(key === 'general' ? {} : { tab: key });

  const [company, setCompany] = useState(null);
  const [canEditIdentity, setCanEditIdentity] = useState(false);
  const [children, setChildren] = useState([]);
  const [form, setForm] = useState(companyToForm(null));

  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [unassigned, setUnassigned] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await fetchCurrentCompany(token);
      const loaded = result?.company ?? null;
      setCompany(loaded);
      setCanEditIdentity(Boolean(result?.canEditIdentity));
      setForm(companyToForm(loaded));
      setDirty(false);
      setUnassigned(false);

      // Alt şirket listesi ikincil: bu çağrı token claim'lerine bağlı olduğu
      // için boş dönebilir, ama sayfanın geri kalanı buna bakmıyor.
      try {
        const list = await fetchCompanies(token);
        setChildren(list.filter((c) => c.parentCompanyId === loaded?.id));
      } catch {
        setChildren([]);
      }
    } catch (err) {
      if (err?.response?.status === 404) {
        setUnassigned(true);
      } else {
        setError(describeError(err, 'Şirket bilgileri okunamadı.'));
      }
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    load();
  }, [load]);

  const profile = useMemo(() => countryProfile(form.countryCode), [form.countryCode]);

  /** Kimlik alanı dolu ve kullanıcının değiştirme yetkisi yoksa kilitli. */
  const locked = (key) => !canEditIdentity && Boolean(company?.[key]);

  const set = (key) => (e) => {
    const value = e.target.value;
    setForm((f) => ({ ...f, [key]: value }));
    setDirty(true);
  };

  const setRow = (listKey, index, key) => (e) => {
    const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
    setForm((f) => {
      const next = f[listKey].slice();
      next[index] = { ...next[index], [key]: value };
      return { ...f, [listKey]: next };
    });
    setDirty(true);
  };

  const addRow = (listKey, factory) => {
    setForm((f) => ({ ...f, [listKey]: [...f[listKey], factory()] }));
    setDirty(true);
  };

  const removeRow = (listKey, index) => {
    setForm((f) => ({ ...f, [listKey]: f[listKey].filter((_, i) => i !== index) }));
    setDirty(true);
  };

  const discard = () => {
    setForm(companyToForm(company));
    setDirty(false);
    setError('');
  };

  const validate = () => {
    if (!form.name.trim()) return 'Şirket adı gerekli.';

    if (form.taxId && profile.taxIdPattern && !profile.taxIdPattern.test(form.taxId.trim())) {
      return `${profile.taxIdLabel} biçimi geçersiz${profile.taxIdHint ? ` (${profile.taxIdHint})` : ''}.`;
    }

    const badIban = form.bankAccounts.find(
      (b) => b.iban && !IBAN_RE.test(b.iban.replace(/\s/g, '').toUpperCase())
    );
    if (badIban) return 'IBAN biçimi geçersiz — ülke kodu + kontrol haneleriyle başlamalı.';

    const emailish = [
      ['generalEmail', 'Genel e-posta'],
      ['dataProtectionOfficerEmail', 'Veri koruma sorumlusu e-postası'],
      ['privacyRepresentativeEmail', 'Gizlilik temsilcisi e-postası'],
    ];
    const badEmail = emailish.find(([key]) => form[key] && !form[key].includes('@'));
    if (badEmail) return `${badEmail[1]} geçerli görünmüyor.`;

    return null;
  };

  const save = async () => {
    const problem = validate();
    if (problem) {
      setError(problem);
      return;
    }

    setSaving(true);
    setError('');
    setNotice('');
    try {
      await updateCompany(token, company.id, {
        ...form,
        fiscalYearStartMonth: form.fiscalYearStartMonth ? Number(form.fiscalYearStartMonth) : null,
        incorporationDate: form.incorporationDate || null,
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
            Kurum kimliği, yasal ve vergi bilgileri, adresler, banka hesapları, belgeler ve alt
            şirket hiyerarşisi.
          </p>
        </div>
        {company?.countryCode && (
          <span className="st-head-meta">{company.countryCode} · {company.legalForm || '—'}</span>
        )}
      </div>

      {!loading && company && (
        <div className="st-tabs" role="tablist">
          {TABS.map((t) => (
            <button
              key={t.key}
              type="button"
              className={tab === t.key ? 'is-active' : ''}
              onClick={() => setTab(t.key)}
            >
              {t.label}
              {t.key === 'tree' && children.length > 0 && ` (${children.length})`}
            </button>
          ))}
        </div>
      )}

      {error && <div className="st-alert">{error}</div>}
      {notice && <div className="st-ok">{notice}</div>}
      {loading && <div className="st-card st-empty">Yükleniyor…</div>}

      {!loading && unassigned && (
        <div className="st-card">
          <p className="st-empty">
            Hesabınız henüz bir şirkete bağlı değil. Çalışma alanınızı kurduktan sonra bu sayfa
            şirketinizin ayarlarını gösterir.
          </p>
        </div>
      )}

      {!loading && company && tab === 'general' && (
        <div className="st-grid-2">
          <section className="st-card">
            <p className="st-caps">Kimlik</p>
            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr' }}>
              <Field label="Görünen ad" value={form.name} onChange={set('name')}
                hint="Panelde ve raporlarda bu ad kullanılır; yasal addan farklı olabilir." />
            </div>
            <div className="st-form-grid" style={{ marginTop: 14 }}>
              <Field label="Ekip büyüklüğü" value={form.teamSize} onChange={set('teamSize')}
                placeholder="6–20" />
              <Field label="Hiyerarşi seviyesi" value={String(company.level ?? 0)} mono disabled />
            </div>
            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr', marginTop: 14 }}>
              <Field label="Açıklama">
                <textarea rows="3" value={form.description} onChange={set('description')} />
              </Field>
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Bölgesel biçimler</p>
            <div className="st-form-grid">
              <Field label="Para birimi">
                <select value={form.baseCurrency} onChange={set('baseCurrency')}>
                  <option value="">Seçin…</option>
                  {CURRENCIES.map((c) => (
                    <option key={c.code} value={c.code}>{c.name}</option>
                  ))}
                </select>
              </Field>
              <Field label="Dil">
                <select value={form.locale} onChange={set('locale')}>
                  <option value="">Seçin…</option>
                  {LOCALES.map((l) => (
                    <option key={l.code} value={l.code}>{l.name}</option>
                  ))}
                </select>
              </Field>
              <Field label="Saat dilimi">
                <select value={form.timeZoneId} onChange={set('timeZoneId')}>
                  <option value="">Seçin…</option>
                  {TIME_ZONES.map((z) => (
                    <option key={z} value={z}>{z}</option>
                  ))}
                </select>
              </Field>
              <Field label="Mali yıl başlangıcı">
                <select value={form.fiscalYearStartMonth} onChange={set('fiscalYearStartMonth')}>
                  <option value="">Ocak (varsayılan)</option>
                  {MONTHS.map((m, i) => (
                    <option key={m} value={String(i + 1)}>{m}</option>
                  ))}
                </select>
              </Field>
            </div>
            <p className="st-card-sub" style={{ marginTop: 14 }}>
              Raporlarda ve dışa aktarımlarda kullanılacak varsayılanlar.
            </p>
          </section>
        </div>
      )}

      {!loading && company && tab === 'legal' && (
        <>
          {!canEditIdentity && (
            <div className="st-note">
              <i>i</i>
              <div>
                Kilit işaretli alanlar şirket kaydı oluşturulurken belirlendi ve değiştirilemez.
                Boş olanları bir kez doldurabilirsiniz; kaydettikten sonra onlar da kilitlenir.
                Hatalı bir kaydı düzeltmek için platform ekibine başvurun.
              </div>
            </div>
          )}

          <section className="st-card">
            <p className="st-caps">Tüzel kimlik</p>
            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr' }}>
              <Field label="Yasal ad" value={form.legalName} onChange={set('legalName')}
                locked={locked('legalName')} hint="Sicilde kayıtlı tam unvan." />
            </div>
            <div className="st-form-grid" style={{ marginTop: 14 }}>
              <Field label="Kısa kod" value={form.code} onChange={set('code')}
                locked={locked('code')} mono />
              <Field label="Ülke" locked={locked('countryCode')}>
                <select value={form.countryCode} onChange={set('countryCode')}
                  disabled={locked('countryCode')}>
                  <option value="">Seçin…</option>
                  {COUNTRIES.map((c) => (
                    <option key={c.code} value={c.code}>{c.name}</option>
                  ))}
                </select>
              </Field>
              <Field label="Tüzel yapı" locked={locked('legalForm')}>
                <select value={form.legalForm} onChange={set('legalForm')}
                  disabled={locked('legalForm')}>
                  <option value="">Seçin…</option>
                  {profile.legalForms.map((f) => (
                    <option key={f.code} value={f.code}>{f.name}</option>
                  ))}
                </select>
              </Field>
              <Field label="Kuruluş tarihi" locked={locked('incorporationDate')}>
                <input type="date" value={form.incorporationDate}
                  onChange={set('incorporationDate')} disabled={locked('incorporationDate')} />
              </Field>
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Vergi ve sicil</p>
            <div className="st-form-grid">
              <Field label={profile.taxIdLabel} value={form.taxId} onChange={set('taxId')}
                locked={locked('taxId')} mono hint={profile.taxIdHint} />
              <Field label={profile.registrationLabel} value={form.registrationNumber}
                onChange={set('registrationNumber')} locked={locked('registrationNumber')} mono />
              <Field label={profile.secondaryRegistrationLabel}
                value={form.secondaryRegistrationNumber}
                onChange={set('secondaryRegistrationNumber')} mono />
              {profile.showTaxOffice && (
                <Field label={profile.taxOfficeLabel} value={form.taxOffice}
                  onChange={set('taxOffice')} />
              )}
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Uluslararası tanımlayıcılar ve faaliyet</p>
            <div className="st-form-grid">
              <Field label="LEI kodu" value={form.leiCode} onChange={set('leiCode')} mono
                hint="ISO 17442 — küresel tüzel kişi tanımlayıcısı." />
              <Field label="DUNS numarası" value={form.dunsNumber} onChange={set('dunsNumber')} mono />
              <Field label="Faaliyet sınıflandırması">
                <select value={form.industryScheme} onChange={set('industryScheme')}>
                  <option value="">Seçin…</option>
                  {INDUSTRY_SCHEMES.map((s) => (
                    <option key={s.code} value={s.code}>{s.name}</option>
                  ))}
                </select>
              </Field>
              <Field label="Faaliyet kodu" value={form.industryCode} onChange={set('industryCode')} mono />
            </div>
            <div className="st-form-grid" style={{ gridTemplateColumns: '1fr', marginTop: 14 }}>
              <Field label="Faaliyet açıklaması">
                <textarea rows="2" value={form.industryDescription}
                  onChange={set('industryDescription')} />
              </Field>
            </div>
          </section>
        </>
      )}

      {!loading && company && tab === 'contact' && (
        <>
          <section className="st-card">
            <p className="st-caps">Genel iletişim</p>
            <div className="st-form-grid">
              <Field label="Telefon" value={form.generalPhone} onChange={set('generalPhone')}
                hint="Ülke koduyla: +90…" />
              <Field label="E-posta" value={form.generalEmail} onChange={set('generalEmail')} />
              <Field label="Web sitesi" value={form.website} onChange={set('website')} />
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Elektronik fatura</p>
            <div className="st-form-grid">
              <Field label="Ağ" value={form.eInvoiceScheme} onChange={set('eInvoiceScheme')}
                placeholder={profile.eInvoiceScheme}
                hint="KEP (TR), PEC (IT), PEPPOL (AB)." />
              <Field label={profile.eInvoiceLabel} value={form.eInvoiceAddress}
                onChange={set('eInvoiceAddress')} mono />
            </div>
          </section>

          <section className="st-card">
            <p className="st-caps">Temsilciler ve uyum</p>
            <div className="st-form-grid">
              <Field label="Yetkili imza sahibi" value={form.authorizedSignatoryName}
                onChange={set('authorizedSignatoryName')} />
              <Field label="Yetkilinin unvanı" value={form.authorizedSignatoryTitle}
                onChange={set('authorizedSignatoryTitle')} />
              <Field label="Veri koruma sorumlusu" value={form.dataProtectionOfficerName}
                onChange={set('dataProtectionOfficerName')}
                hint="GDPR md. 37 / KVKK irtibat kişisi." />
              <Field label="Veri koruma sorumlusu e-postası"
                value={form.dataProtectionOfficerEmail}
                onChange={set('dataProtectionOfficerEmail')} />
              <Field label="Yurt dışı temsilcisi" value={form.privacyRepresentativeName}
                onChange={set('privacyRepresentativeName')} hint="GDPR md. 27." />
              <Field label="Temsilci e-postası" value={form.privacyRepresentativeEmail}
                onChange={set('privacyRepresentativeEmail')} />
              <Field label="Temsilcinin ülkesi">
                <select value={form.privacyRepresentativeCountryCode}
                  onChange={set('privacyRepresentativeCountryCode')}>
                  <option value="">Seçin…</option>
                  {COUNTRIES.map((c) => (
                    <option key={c.code} value={c.code}>{c.name}</option>
                  ))}
                </select>
              </Field>
            </div>
          </section>
        </>
      )}

      {!loading && company && tab === 'addresses' && (
        <section className="st-card">
          <div className="st-card-head">
            <div>
              <h2>Adresler</h2>
              <p className="st-card-sub">
                Tebligat, fatura ve operasyon adresleri ayrı tutulur; hepsi aynı olabilir.
              </p>
            </div>
            <button type="button" className="st-btn st-btn--sm"
              onClick={() => addRow('addresses', emptyAddress)}>
              + Adres ekle
            </button>
          </div>

          {form.addresses.length === 0 ? (
            <p className="st-empty">Henüz adres eklenmedi.</p>
          ) : (
            form.addresses.map((a, i) => (
              <div key={i} className="st-repeat">
                <div className="st-repeat-head">
                  <select value={a.type} onChange={setRow('addresses', i, 'type')}>
                    {ADDRESS_TYPES.map((t) => (
                      <option key={t.code} value={t.code}>{t.name}</option>
                    ))}
                  </select>
                  <label className="st-check">
                    <input type="checkbox" checked={a.isPrimary}
                      onChange={setRow('addresses', i, 'isPrimary')} />
                    Birincil
                  </label>
                  <button type="button" className="st-link"
                    onClick={() => removeRow('addresses', i)}>Kaldır</button>
                </div>
                <div className="st-form-grid">
                  <Field label="Adres satırı 1" value={a.line1}
                    onChange={setRow('addresses', i, 'line1')} />
                  <Field label="Adres satırı 2" value={a.line2}
                    onChange={setRow('addresses', i, 'line2')} />
                  <Field label="Şehir" value={a.city} onChange={setRow('addresses', i, 'city')} />
                  <Field label="İl / eyalet / bölge" value={a.region}
                    onChange={setRow('addresses', i, 'region')} />
                  <Field label="Posta kodu" value={a.postalCode}
                    onChange={setRow('addresses', i, 'postalCode')} mono />
                  <Field label="Ülke">
                    <select value={a.countryCode} onChange={setRow('addresses', i, 'countryCode')}>
                      <option value="">Seçin…</option>
                      {COUNTRIES.map((c) => (
                        <option key={c.code} value={c.code}>{c.name}</option>
                      ))}
                    </select>
                  </Field>
                </div>
              </div>
            ))
          )}
        </section>
      )}

      {!loading && company && tab === 'bank' && (
        <section className="st-card">
          <div className="st-card-head">
            <div>
              <h2>Banka hesapları</h2>
              <p className="st-card-sub">
                {profile.usesIban
                  ? 'IBAN kullanan ülkeler için IBAN, diğerleri için hesap numarası + yönlendirme kodu girin.'
                  : 'Bu ülkede IBAN kullanılmıyor; hesap numarası ve yönlendirme kodu girin.'}
              </p>
            </div>
            <button type="button" className="st-btn st-btn--sm"
              onClick={() => addRow('bankAccounts', emptyBankAccount)}>
              + Hesap ekle
            </button>
          </div>

          {form.bankAccounts.length === 0 ? (
            <p className="st-empty">Henüz banka hesabı eklenmedi.</p>
          ) : (
            form.bankAccounts.map((b, i) => (
              <div key={i} className="st-repeat">
                <div className="st-repeat-head">
                  <strong>{b.bankName || `Hesap ${i + 1}`}</strong>
                  <label className="st-check">
                    <input type="checkbox" checked={b.isPrimary}
                      onChange={setRow('bankAccounts', i, 'isPrimary')} />
                    Birincil
                  </label>
                  <button type="button" className="st-link"
                    onClick={() => removeRow('bankAccounts', i)}>Kaldır</button>
                </div>
                <div className="st-form-grid">
                  <Field label="Banka" value={b.bankName}
                    onChange={setRow('bankAccounts', i, 'bankName')} />
                  <Field label="Şube" value={b.branchName}
                    onChange={setRow('bankAccounts', i, 'branchName')} />
                  <Field label="Hesap sahibi" value={b.accountHolder}
                    onChange={setRow('bankAccounts', i, 'accountHolder')} />
                  <Field label="IBAN" value={b.iban} onChange={setRow('bankAccounts', i, 'iban')}
                    mono placeholder="TR…" />
                  <Field label="Hesap numarası" value={b.accountNumber}
                    onChange={setRow('bankAccounts', i, 'accountNumber')} mono
                    hint="IBAN kullanmayan ülkeler için." />
                  <Field label="Yönlendirme kodu" value={b.routingCode}
                    onChange={setRow('bankAccounts', i, 'routingCode')} mono
                    hint="ABA (US), sort code (GB), BSB (AU)." />
                  <Field label="SWIFT / BIC" value={b.swiftBic}
                    onChange={setRow('bankAccounts', i, 'swiftBic')} mono />
                  <Field label="Para birimi">
                    <select value={b.currency} onChange={setRow('bankAccounts', i, 'currency')}>
                      <option value="">Seçin…</option>
                      {CURRENCIES.map((c) => (
                        <option key={c.code} value={c.code}>{c.name}</option>
                      ))}
                    </select>
                  </Field>
                  <Field label="Banka ülkesi">
                    <select value={b.countryCode}
                      onChange={setRow('bankAccounts', i, 'countryCode')}>
                      <option value="">Seçin…</option>
                      {COUNTRIES.map((c) => (
                        <option key={c.code} value={c.code}>{c.name}</option>
                      ))}
                    </select>
                  </Field>
                </div>
              </div>
            ))
          )}
        </section>
      )}

      {!loading && company && FORM_TABS.includes(tab) && (
        <div className="st-savebar">
          <span className="st-savebar-note">
            {dirty ? 'kaydedilmemiş değişiklikler var' : 'güncel'}
          </span>
          <span className="st-savebar-actions">
            <button type="button" className="st-btn st-btn--ghost" disabled={!dirty || saving}
              onClick={discard}>
              Vazgeç
            </button>
            <button type="button" className="st-btn" disabled={saving} onClick={save}>
              {saving ? 'Kaydediliyor…' : 'Kaydet'}
            </button>
          </span>
        </div>
      )}

      {!loading && company && tab === 'documents' && (
        <DocumentsPanel token={token} companyId={company.id}
          setError={setError} setNotice={setNotice} />
      )}

      {!loading && company && tab === 'tree' && (
        <SubCompaniesPanel token={token} parentId={company.id} items={children}
          onCreated={load} setError={setError} setNotice={setNotice} />
      )}
    </div>
  );
}

/**
 * Etiket + giriş kutusu. Kilitli alanlar görsel olarak da işaretleniyor:
 * yalnızca devre dışı bırakmak, alanın neden düzenlenemediğini söylemiyor.
 */
function Field({ label, value, onChange, locked, disabled, mono, hint, placeholder, children }) {
  return (
    <label className={`st-field ${mono ? 'st-field--mono' : ''}`}>
      <span>
        {label}
        {locked && (
          <span className="st-lock" title="Şirket kaydı oluşturulurken belirlendi">kilitli</span>
        )}
      </span>
      {children ?? (
        <input
          value={value}
          onChange={onChange}
          disabled={locked || disabled}
          placeholder={placeholder}
        />
      )}
      {hint && <small className="st-hint">{hint}</small>}
    </label>
  );
}

/** Belge listesi + önizlemeli kart ızgarası. */
function DocumentsPanel({ token, companyId, setError, setNotice }) {
  const [documents, setDocuments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [file, setFile] = useState(null);
  const [form, setForm] = useState({
    documentType: COMPANY_DOCUMENT_TYPES[0].code,
    expiryDate: '',
    note: '',
  });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setDocuments(await fetchCompanyDocuments(token, companyId));
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
      e.target.reset();
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

  return (
    <>
      <section className="st-card">
        <div className="st-card-head">
          <div>
            <h2>Belgeler</h2>
            <p className="st-card-sub">
              Vergi levhası, imza sirküleri, sicil kaydı gibi kurumsal evrak.
            </p>
          </div>
          <span className="st-head-meta">{documents.length} belge</span>
        </div>

        {loading && <p className="st-empty">Yükleniyor…</p>}
        {!loading && documents.length === 0 && (
          <p className="st-empty">Henüz belge yüklenmedi.</p>
        )}

        {!loading && documents.length > 0 && (
          <div className="st-doc-grid">
            {documents.map((doc) => (
              <DocumentCard key={doc.id} doc={doc} token={token} companyId={companyId}
                onDownload={() => download(doc)} onRemove={() => remove(doc)} />
            ))}
          </div>
        )}
      </section>

      <section className="st-card">
        <p className="st-caps">Belge yükle</p>
        <form onSubmit={submit} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <div className="st-form-grid">
            <Field label="Belge türü">
              <select value={form.documentType}
                onChange={(e) => setForm({ ...form, documentType: e.target.value })}>
                {COMPANY_DOCUMENT_TYPES.map((t) => (
                  <option key={t.code} value={t.code}>{t.name}</option>
                ))}
              </select>
            </Field>
            <Field label="Geçerlilik tarihi" hint="İsteğe bağlı — süresi dolunca uyarılırsınız.">
              <input type="date" value={form.expiryDate}
                onChange={(e) => setForm({ ...form, expiryDate: e.target.value })} />
            </Field>
            <Field label="Dosya" hint="PDF ve resimlerin önizlemesi otomatik üretilir.">
              <input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} />
            </Field>
            <Field label="Not" value={form.note}
              onChange={(e) => setForm({ ...form, note: e.target.value })} />
          </div>
          <button type="submit" className="st-btn" disabled={uploading}
            style={{ alignSelf: 'flex-start' }}>
            {uploading ? 'Yükleniyor…' : '+ Belge yükle'}
          </button>
        </form>
      </section>
    </>
  );
}

const fmtDate = (str) =>
  str
    ? new Date(str).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
    : '—';

const fmtSize = (bytes) => {
  if (!bytes) return '—';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
};

const expiryState = (doc) => {
  if (!doc.expiryDate) return null;
  const days = (new Date(doc.expiryDate) - new Date()) / 86400000;
  if (days < 0) return { cls: 'st-badge--err', text: 'Süresi doldu' };
  if (days < 30) return { cls: 'st-badge--warn', text: 'Yakında dolacak' };
  return { cls: 'st-badge--ok', text: 'Geçerli' };
};

function DocumentCard({ doc, token, companyId, onDownload, onRemove }) {
  const expiry = expiryState(doc);

  return (
    <article className="st-doc-card">
      <DocumentThumb doc={doc} token={token} companyId={companyId} />
      <div className="st-doc-body">
        <strong className="st-doc-name" title={doc.originalFileName}>{doc.originalFileName}</strong>
        <span className="st-doc-meta">{documentTypeName(doc.documentType)}</span>
        <span className="st-doc-meta st-mono">
          {fmtSize(doc.fileSizeBytes)} · {fmtDate(doc.uploadedAt)}
        </span>
        {expiry && (
          <span className={`st-badge ${expiry.cls}`}>
            {expiry.text} · {fmtDate(doc.expiryDate)}
          </span>
        )}
        {doc.note && <span className="st-doc-meta">{doc.note}</span>}
        <span className="st-doc-actions">
          <button type="button" className="st-link" onClick={onDownload}>İndir</button>
          <button type="button" className="st-link" onClick={onRemove}>Sil</button>
        </span>
      </div>
    </article>
  );
}

/**
 * Önizleme görseli.
 *
 * Doğrudan <img src> kullanılamıyor: uç kimlik doğrulama istiyor ve tarayıcı
 * img isteklerine Authorization başlığı eklemiyor. Blob nesne URL'i bileşen
 * kaldırıldığında serbest bırakılıyor, yoksa her sekme geçişinde bellekte
 * birikir.
 */
function DocumentThumb({ doc, token, companyId }) {
  const [url, setUrl] = useState(null);

  useEffect(() => {
    if (!doc.hasThumbnail) return undefined;

    let cancelled = false;
    let created = null;

    fetchCompanyDocumentThumbnail(token, companyId, doc.id)
      .then((objectUrl) => {
        if (cancelled) {
          window.URL.revokeObjectURL(objectUrl);
          return;
        }
        created = objectUrl;
        setUrl(objectUrl);
      })
      .catch(() => {});

    return () => {
      cancelled = true;
      if (created) window.URL.revokeObjectURL(created);
    };
  }, [doc.id, doc.hasThumbnail, token, companyId]);

  if (url) {
    return <img className="st-doc-thumb" src={url} alt="" loading="lazy" />;
  }

  // Önizlemesi olmayan belge (üretilemedi ya da desteklenmeyen biçim) yine de
  // bir yer kaplamalı; ızgara satırları kaymasın diye aynı ölçüde bir rozet.
  const ext = (doc.originalFileName.split('.').pop() || '?').toUpperCase().slice(0, 4);
  return <span className="st-doc-thumb st-doc-thumb--empty">{ext}</span>;
}

function SubCompaniesPanel({ token, parentId, items, onCreated, setError, setNotice }) {
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
      <div className="st-card-head">
        <div>
          <h2>Alt şirketler</h2>
          <p className="st-card-sub">
            Her alt şirket ayrı tüzel kişilik sayılır ve kendi yasal bilgilerini taşır.
          </p>
        </div>
      </div>

      {items.length === 0 ? (
        <p className="st-empty">Henüz alt şirket yok.</p>
      ) : (
        <div className="st-table-wrap">
          <table className="st-table">
            <thead>
              <tr>
                <th>Ad</th>
                <th>Kısa kod</th>
                <th>Ülke</th>
                <th>Seviye</th>
              </tr>
            </thead>
            <tbody>
              {items.map((c) => (
                <tr key={c.id}>
                  <td className="st-strong">{c.name}</td>
                  <td className="st-mono st-dim">{c.code || '—'}</td>
                  <td className="st-mono st-dim">{c.countryCode || '—'}</td>
                  <td className="st-mono st-dim">{c.level}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <form onSubmit={submit}
        style={{ marginTop: 20, display: 'flex', flexDirection: 'column', gap: 14 }}>
        <p className="st-caps" style={{ margin: 0 }}>Alt şirket ekle</p>
        <div className="st-form-grid">
          <Field label="Ad" value={form.name}
            onChange={(e) => setForm({ ...form, name: e.target.value })}
            placeholder="Akdeniz Tekstil Lojistik" />
          <Field label="Kısa kod" value={form.code}
            onChange={(e) => setForm({ ...form, code: e.target.value })}
            placeholder="AKD-LOJ" mono />
        </div>
        <button type="submit" className="st-btn" disabled={busy}
          style={{ alignSelf: 'flex-start' }}>
          {busy ? 'Ekleniyor…' : '+ Alt şirket ekle'}
        </button>
      </form>
    </section>
  );
}
