import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { describeError, fetchCompanies, fetchCompanyUsers } from '../../services/companyService';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Departman Ayarlari.
 *
 * Identity'de departman diye bir varlik yok: veri modeli sirket → kullanici →
 * rol seklinde. Dolayisiyla taslaktaki departman tablosu (kod, yonetici,
 * maliyet merkezi) doldurulamiyor.
 *
 * Sayfayi "Burasi bir ayar sayfasidir" olarak birakmak yerine, gercekten var
 * olan organizasyon yapisini gosteriyor: sirket agaci ve sayilari. Departman
 * bolumu ne oldugunu ve neyin eksik oldugunu soyleyen bir bos durumla
 * duruyor.
 */
export default function DepartmentsPage() {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();
  const token = keycloak.token;
  const companyId = keycloak.tokenParsed?.company_id;

  const [companies, setCompanies] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!companyId) {
      setLoading(false);
      return undefined;
    }
    let cancelled = false;
    (async () => {
      try {
        const [list, members] = await Promise.all([
          fetchCompanies(token),
          fetchCompanyUsers(token, companyId),
        ]);
        if (!cancelled) {
          setCompanies(list);
          setUsers(members);
        }
      } catch (err) {
        if (!cancelled) setError(describeError(err, 'Organizasyon bilgileri okunamadı.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, companyId]);

  const company = companies.find((c) => c.id === companyId) || null;
  const children = companies.filter((c) => c.parentCompanyId === companyId);

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Ayarlar · Organizasyon</p>
          <h1>Departman Ayarları</h1>
          <p className="st-lead">
            Departmanlar; veri erişimi, rapor dağıtımı ve maliyet merkezi kırılımlarının temelidir.
          </p>
        </div>
      </div>

      {error && <div className="st-alert">{error}</div>}

      <div className="st-split">
        <section className="st-card">
          <div className="st-card-head">
            <h2>Departmanlar</h2>
          </div>
          <p className="st-empty">
            Departman tanımı henüz yok. Identity servisinde veri modeli şirket, kullanıcı ve rol
            üzerinden kurulu; departman ayrı bir varlık olarak tanımlanmadığı için burada
            listelenecek bir kayıt bulunmuyor.
          </p>
          <p className="st-empty" style={{ marginTop: 12 }}>
            O gelene kadar erişim kırılımını <strong>Yetki Ayarları</strong> üzerinden rollerle
            yapabilirsiniz.
          </p>
          <div style={{ marginTop: 18 }}>
            <button
              type="button"
              className="st-btn st-btn--ghost st-btn--sm"
              onClick={() => navigate('/settings/users?tab=roles')}
            >
              Yetki ayarlarına git →
            </button>
          </div>
        </section>

        <div className="st-col">
          <section className="st-card">
            <div className="st-card-head">
              <h2>Hiyerarşi</h2>
            </div>
            {loading ? (
              <p className="st-empty">Yükleniyor…</p>
            ) : !company ? (
              <p className="st-empty">Hesabınız henüz bir şirkete bağlı değil.</p>
            ) : (
              <div className="st-tree">
                <div className="st-tree-root">{company.name}</div>
                {children.length === 0 ? (
                  <div className="st-tree-child">alt şirket yok</div>
                ) : (
                  children.map((c, i) => (
                    <div className="st-tree-child" key={c.id}>
                      {i === children.length - 1 ? '└' : '├'} {c.name}
                    </div>
                  ))
                )}
              </div>
            )}
          </section>

          <section className="st-card">
            <div className="st-card-head">
              <h2>Toplam</h2>
            </div>
            <dl className="st-rows">
              <div>
                <dt>Alt şirket</dt>
                <dd>{loading ? '—' : children.length}</dd>
              </div>
              <div>
                <dt>Yetkili kullanıcı</dt>
                <dd>{loading ? '—' : users.length}</dd>
              </div>
              <div>
                <dt>Departman</dt>
                <dd className="st-dim">tanımsız</dd>
              </div>
            </dl>
          </section>
        </div>
      </div>
    </div>
  );
}
