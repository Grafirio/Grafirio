import React, { useEffect, useState } from 'react';
import { Navigate, Outlet } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { useAuth } from '../contexts/AuthContext';
import MainLayout from '../layouts/MainLayout';
import { describeAccessReason, fetchMyAccess } from '../services/accessService';

const ProtectedRoute = () => {
  const { isAuthenticated } = useAuth();
  const { keycloak } = useKeycloak();

  // Giris yetmiyor: firmanin urunu satin almis olmasi da gerekiyor.
  // null = henuz sorulmadi.
  const [access, setAccess] = useState(null);
  const [accessError, setAccessError] = useState('');

  useEffect(() => {
    if (!isAuthenticated) return undefined;

    let cancelled = false;

    (async () => {
      try {
        const result = await fetchMyAccess(keycloak.token);
        if (!cancelled) setAccess(result);
      } catch (error) {
        // Kontrol edilemiyorsa kullanici disari atilmaz: gecici bir ag ya da
        // servis sorunu yuzunden odenmis bir hesabi kilitlemek, birkac dakika
        // fazladan erisimden daha kotu.
        console.error('Erişim kontrolü yapılamadı:', error);
        if (!cancelled) {
          setAccessError('Abonelik durumu doğrulanamadı; erişim geçici olarak sürdürülüyor.');
          setAccess({ hasAccess: true, degraded: true });
        }
      }
    })();

    return () => { cancelled = true; };
  }, [isAuthenticated, keycloak.token]);

  if (!isAuthenticated) {
    // Kullanıcı giriş yapmamışsa login sayfasına yönlendir.
    return <Navigate to="/login" replace />;
  }

  if (access === null) {
    return (
      <div className="gf-page">
        <div className="gf-stack">
          <div className="gf-skeleton gf-skeleton--title" />
          <div className="gf-skeleton gf-skeleton--line" />
          <div className="gf-skeleton gf-skeleton--line gf-skeleton--short" />
        </div>
      </div>
    );
  }

  if (!access.hasAccess) {
    return (
      <div className="gf-page">
        <div className="gf-empty">
          <i className="ti ti-lock" />
          <h3>Ürüne erişiminiz bulunmuyor</h3>
          <p>{describeAccessReason(access.reason)}</p>
          {/* Firmasi olmayan kullanici cikmaza dusmesin: eksik olan sey tam
              da karsilama sihirbazinin kurdugu sey. */}
          {access.reason === 'User is not assigned to a company' ? (
            <>
              <p className="gf-text-sm gf-subtle">
                Çalışma alanınızı kurup paketinizi seçerek başlayabilirsiniz.
              </p>
              <a className="gf-btn" href="/onboarding">Çalışma alanı kur</a>
            </>
          ) : (
            <p className="gf-text-sm gf-subtle">
              Devam etmek için firmanızın yöneticisiyle veya bizimle iletişime geçin.
            </p>
          )}
          <button className="gf-btn" onClick={() => keycloak.logout()}>Çıkış yap</button>
        </div>
      </div>
    );
  }

  // Giriş yapmışsa, ana layout içinde ilgili sayfayı göster.
  return (
    <MainLayout>
      {accessError && <div className="gf-alert gf-alert--warning">{accessError}</div>}
      <Outlet />
    </MainLayout>
  );
};

export default ProtectedRoute;
