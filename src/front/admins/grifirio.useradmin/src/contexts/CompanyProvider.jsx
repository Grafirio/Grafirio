import { useCallback, useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import { fetchAccessibleCompanies, fetchMyPermissions } from '../services/companyService';
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

  // İzinler seçili şirkete bağlı: aynı kullanıcı bir şubede yönetici, başka
  // birinde sıradan kullanıcı olabiliyor. Şirket değişince yeniden okunuyor.
  const [permissions, setPermissions] = useState(null);
  const [permissionsLoading, setPermissionsLoading] = useState(true);

  const loadPermissions = useCallback(async () => {
    if (!token || !selectedId) {
      setPermissions(null);
      setPermissionsLoading(false);
      return;
    }
    setPermissionsLoading(true);
    try {
      setPermissions(await fetchMyPermissions(token, selectedId));
    } catch {
      // İzin okunamazsa menüyü boş bırakmıyoruz: sunucu her isteği zaten
      // kendisi denetliyor, burada kilitlemek kullanıcıyı boş bir panele
      // düşürürdü.
      setPermissions(null);
    } finally {
      setPermissionsLoading(false);
    }
  }, [token, selectedId]);

  useEffect(() => {
    loadPermissions();
  }, [loadPermissions]);

  const value = useMemo(() => {
    const modules = permissions?.modules ?? null;
    const granted = permissions?.permissions ?? null;

    return {
      companies,
      selectedId,
      selected: companies.find((c) => c.id === selectedId) ?? null,
      loading,
      error,
      selectCompany: setSelectedId,
      reload: load,

      role: permissions?.role ?? null,
      modules,
      permissions: granted,
      restrictedByDepartment: permissions?.restrictedByDepartment ?? false,
      permissionsLoading,
      reloadPermissions: loadPermissions,

      // İzinler henüz okunmadıysa (ya da okunamadıysa) menü gizlenmiyor;
      // sunucu zaten reddediyor, erken gizlemek yanlış boşluk yaratır.
      //
      // canModule: menü ve kart görünürlüğü — "bu ekrana girebilir mi".
      // can: ekran içindeki buton — "bu işi yapabilir mi". İkisi ayrı, çünkü
      // bir modülü görmek onu değiştirebilmek anlamına gelmiyor; matris tam
      // olarak bu ayrımı ayarlanabilir yapmak için var.
      canModule: (module) => (modules === null ? true : modules.includes(module)),
      can: (permission) => (granted === null ? true : granted.includes(permission)),
    };
  }, [companies, selectedId, loading, error, load, permissions, permissionsLoading, loadPermissions]);

  return <CompanyContext.Provider value={value}>{children}</CompanyContext.Provider>;
}
