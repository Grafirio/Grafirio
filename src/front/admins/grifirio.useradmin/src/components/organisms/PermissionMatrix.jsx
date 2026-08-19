import { moduleName } from '../../constants/modules';
import { actionLabel, permissionHint } from '../../constants/permissions';
import '../../styles/SettingsPages.css';

/**
 * Kaynak × aksiyon izin matrisi.
 *
 * Iki yerde ayni bilesen kullaniliyor: rolun izin kumesini duzenlerken ve
 * kisinin izinlerini duzenlerken. Ikisi ayri yazilsaydi ayni izin iki ekranda
 * iki turlu gorunurdu.
 *
 * Satirlar ve sutunlar sunucudan geliyor (GET /permissions/actions): modullerin
 * aksiyonlari ayni degil (uyelikte "degistirme" yok, kullanicilarda "uyelik
 * yonetimi" var), dolayisiyla sabit sutunlu gercek bir tablo cogu hucreyi bos
 * birakirdi.
 *
 * @param catalog  [{ module, permissions: ['ROLES.READ', ...] }]
 * @param value    duzenlenebilir izinler (rolde: rolun izinleri, kiside: kisisel)
 * @param onChange yeni liste ile cagriliyor; verilmezse matris salt okunur
 * @param locked   rolden gelen izinler: acik gosteriliyor ama kapatilamiyor.
 *                 Kapatmak icin rolu almak gerekiyor, tek bir anahtari
 *                 kapatmak rolun tanimini o kisi icin bozardi.
 */
export default function PermissionMatrix({
  catalog,
  value = [],
  onChange,
  locked = [],
  emptyText = 'İzin listesi okunamadı.',
}) {
  const readOnly = typeof onChange !== 'function';
  const own = new Set(value);
  const fromRoles = new Set(locked);

  if (!catalog || catalog.length === 0) {
    return <p className="st-empty">{emptyText}</p>;
  }

  const toggle = (key, on) => {
    onChange(on ? value.filter((k) => k !== key) : [...new Set([...value, key])]);
  };

  /** Modul basligi yalnizca duzenlenebilir olanlari aciyor/kapatiyor. */
  const toggleModule = (keys) => {
    const editable = keys.filter((k) => !fromRoles.has(k));
    const allOn = editable.every((k) => own.has(k));

    onChange(
      allOn
        ? value.filter((k) => !editable.includes(k))
        : [...new Set([...value, ...editable])]
    );
  };

  return (
    <div className="st-perm-list" data-locked={readOnly}>
      {catalog.map(({ module, permissions: keys }) => {
        // Etkin sayim: rolden gelenler de aciktir, sayiya girmeliler — aksi
        // halde "0/6 izin" yazan bir modulde acik anahtarlar gorunurdu.
        const on = keys.filter((k) => own.has(k) || fromRoles.has(k));
        const editable = keys.filter((k) => !fromRoles.has(k));
        const allEditableOn = editable.length > 0 && editable.every((k) => own.has(k));
        const some = on.length > 0;

        return (
          <div key={module} className="st-perm-module" data-on={some}>
            <div className="st-perm-module-head">
              <label>
                <input
                  type="checkbox"
                  checked={allEditableOn}
                  disabled={readOnly || editable.length === 0}
                  // Kismi secim ucuncu bir durum: kutu isaretli degil ama
                  // "hicbiri" de degil. Isaretsiz gostermek yanlis bilgi verir.
                  ref={(el) => {
                    if (el) el.indeterminate = some && !allEditableOn;
                  }}
                  onChange={() => toggleModule(keys)}
                />
                <strong>{moduleName(module)}</strong>
              </label>
              <small>
                {on.length}/{keys.length} izin
              </small>
            </div>

            <div className="st-perm-actions">
              {keys.map((key) => {
                const viaRole = fromRoles.has(key);
                const isOn = viaRole || own.has(key);

                return (
                  <label
                    key={key}
                    className="st-perm-switch"
                    data-on={isOn}
                    data-inherited={viaRole}
                    title={
                      viaRole
                        ? 'Bu izin bir rolden geliyor. Kaldırmak için rolü geri alın.'
                        : permissionHint(key) ?? key
                    }
                  >
                    <input
                      type="checkbox"
                      checked={isOn}
                      disabled={readOnly || viaRole}
                      onChange={() => toggle(key, own.has(key))}
                    />
                    <span className="st-perm-knob" aria-hidden="true" />
                    <span className="st-perm-switch-label">{actionLabel(key)}</span>
                  </label>
                );
              })}
            </div>
          </div>
        );
      })}
    </div>
  );
}
