import { useCallback, useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { fetchAccessibleCompanies } from '../services/companyService';
import { CompanyContext, SELECTED_COMPANY_KEY } from './companyContext';

/**
 * Kullanicinin girebildigi sirketler ve o an secili olani.
 *
 * Secim sunucuda degil tarayicida tutuluyor: panelin butun uclari sirket
 * kimligini zaten acikca aliyor, dolayisiyla "su an hangi sirketteyim" bir
 * sunucu durumu degil bir gorunum tercihi. Boylece ayni kullanici iki sekmede
 * iki farkli subeye bakabiliyor ve secim token'a hic dokunmuyor.
 *
 * Yetki yine sunucuda: secilen kimlik dogrudan da gonderilebilir, uclar her
 * istekte erisimi kendileri dogruluyor.
 */
export function CompanyProvider({ children }) {
  const { keycloak } = useKeycloak();
  const token = keycloak?.token;

  const [companies, setCompanies] = useState([]);
  const [selectedId, setSelectedId] = useState(
    () => localStorage.getItem(SELECTED_COMPANY_KEY) || null
  );
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    // Onboarding gibi henuz oturum acilmamis ekranlarda token yok; beklemeden
    // bitiriyoruz ki tuketiciler sonsuza kadar "yukleniyor" gormesin.
    if (!token) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const list = await fetchAccessibleCompanies(token);
      setCompanies(list);

      // Hatirlanan secim artik erisilebilir degilse (yetki kaldirilmis ya da
      // sirket kapanmis olabilir) sessizce ilkine donuluyor; aksi halde panel
      // erisilemeyen bir sirkete kilitlenip her sayfada 403 gosterirdi.
      setSelectedId((current) => {
        if (current && list.some((c) => c.id === current)) return current;
        return list[0]?.id ?? null;
      });
    } catch (err) {
      setError(err?.response?.status === 401 ? '' : 'Şirket listesi okunamadı.');
      setCompanies([]);
    } finally {
      setLoading(false);
    }
  }, [token]);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    if (selectedId) localStorage.setItem(SELECTED_COMPANY_KEY, selectedId);
    else localStorage.removeItem(SELECTED_COMPANY_KEY);
  }, [selectedId]);

  const value = useMemo(
    () => ({
      companies,
      selectedId,
      selected: companies.find((c) => c.id === selectedId) ?? null,
      loading,
      error,
      selectCompany: setSelectedId,
      reload: load,
    }),
    [companies, selectedId, loading, error, load]
  );

  return <CompanyContext.Provider value={value}>{children}</CompanyContext.Provider>;
}
