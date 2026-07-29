import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createCompany, describeError, getCompanies } from '../services/identityApi';

/**
 * Duz listeyi agaca cevirir. Sunucu Level'i zaten hesapliyor ama sirayi
 * garanti etmiyor; ust firmasi listede olmayan kayitlar (erisim kapsami
 * disinda kalmis olabilir) kok seviyesinde gosterilir ki gizlenmesinler.
 */
const buildTree = (companies) => {
  const byId = new Map(companies.map((c) => [c.id, { ...c, children: [] }]));
  const roots = [];

  for (const node of byId.values()) {
    const parent = node.parentCompanyId ? byId.get(node.parentCompanyId) : null;
    if (parent) parent.children.push(node);
    else roots.push(node);
  }

  const sortByName = (nodes) => {
    nodes.sort((a, b) => a.name.localeCompare(b.name, 'tr'));
    nodes.forEach((n) => sortByName(n.children));
  };
  sortByName(roots);

  return roots;
};

function CompanyRow({ node, depth, onAddChild, onOpen }) {
  return (
    <>
      <div className="pa-tree__row" style={{ paddingLeft: `calc(${depth} * var(--space-5))` }}>
        <button className="pa-tree__name" onClick={() => onOpen(node)}>
          {node.name}
        </button>
        {node.code && <span className="gf-badge">{node.code}</span>}
        {!node.isActive && <span className="gf-badge gf-badge--danger">pasif</span>}
        <span className="pa-tree__spacer" />
        <button className="gf-btn gf-btn--sm gf-btn--ghost" onClick={() => onAddChild(node)}>
          Alt firma ekle
        </button>
      </div>
      {node.children.map((child) => (
        <CompanyRow
          key={child.id}
          node={child}
          depth={depth + 1}
          onAddChild={onAddChild}
          onOpen={onOpen}
        />
      ))}
    </>
  );
}

const EMPTY_FORM = { name: '', code: '', description: '', parentCompanyId: null };

export default function CompaniesPage() {
  const navigate = useNavigate();
  const [companies, setCompanies] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [form, setForm] = useState(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setCompanies(await getCompanies());
    } catch (err) {
      setError(describeError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const submit = async (event) => {
    event.preventDefault();
    setSaving(true);
    setFormError('');
    try {
      await createCompany(form);
      setForm(null);
      await load();
    } catch (err) {
      setFormError(describeError(err));
    } finally {
      setSaving(false);
    }
  };

  const parentName = form?.parentCompanyId
    ? companies.find((c) => c.id === form.parentCompanyId)?.name
    : null;

  return (
    <div className="gf-page">
      <div className="gf-page-header">
        <div>
          <h1 className="gf-page-title">Firmalar</h1>
          <p className="gf-page-subtitle">
            Müşteri firmalarını ve alt firmalarını yönetin
          </p>
        </div>
        <button className="gf-btn gf-btn--primary" onClick={() => setForm({ ...EMPTY_FORM })}>
          Yeni firma
        </button>
      </div>

      {form && (
        <form className="gf-card pa-form" onSubmit={submit}>
          <div className="gf-card__header">
            <h3>{parentName ? `${parentName} altına alt firma` : 'Yeni kök firma'}</h3>
          </div>
          <div className="gf-card__body gf-stack">
            {formError && <div className="gf-alert gf-alert--danger">{formError}</div>}
            <div className="gf-field">
              <label className="gf-label" htmlFor="name">Firma adı</label>
              <input
                id="name"
                className="gf-input"
                required
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
              />
            </div>
            <div className="gf-field">
              <label className="gf-label" htmlFor="code">Kod</label>
              <input
                id="code"
                className="gf-input"
                value={form.code}
                onChange={(e) => setForm({ ...form, code: e.target.value })}
              />
            </div>
            <div className="gf-field">
              <label className="gf-label" htmlFor="description">Açıklama</label>
              <input
                id="description"
                className="gf-input"
                value={form.description}
                onChange={(e) => setForm({ ...form, description: e.target.value })}
              />
            </div>
          </div>
          <div className="gf-card__footer">
            <button
              type="button"
              className="gf-btn gf-btn--ghost"
              onClick={() => setForm(null)}
              disabled={saving}
            >
              İptal
            </button>
            <button type="submit" className="gf-btn gf-btn--primary" disabled={saving}>
              {saving ? <><span className="gf-spinner" /> Kaydediliyor…</> : 'Kaydet'}
            </button>
          </div>
        </form>
      )}

      {loading ? (
        <div className="gf-card gf-stack" style={{ padding: 'var(--space-5)' }}>
          {[0, 1, 2].map((i) => (
            <div key={i} className="gf-skeleton gf-skeleton--line" />
          ))}
        </div>
      ) : error ? (
        <div className="gf-alert gf-alert--danger">
          <div className="gf-stack" style={{ gap: 'var(--space-3)' }}>
            <span>{error}</span>
            <button className="gf-btn gf-btn--sm" onClick={load}>Tekrar dene</button>
          </div>
        </div>
      ) : companies.length === 0 ? (
        <div className="gf-empty">
          <h3>Henüz firma yok</h3>
          <p>İlk müşteri firmasını oluşturarak başlayın.</p>
        </div>
      ) : (
        <div className="gf-card pa-tree">
          {buildTree(companies).map((node) => (
            <CompanyRow
              key={node.id}
              node={node}
              depth={0}
              onAddChild={(parent) => setForm({ ...EMPTY_FORM, parentCompanyId: parent.id })}
              onOpen={(c) => navigate(`/companies/${c.id}`)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
