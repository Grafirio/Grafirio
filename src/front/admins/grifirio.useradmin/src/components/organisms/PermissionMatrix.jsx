import { moduleName } from '../../constants/modules';
import { actionLabel, permissionHint } from '../../constants/permissions';
import '../../styles/SettingsPages.css';

/**
 * Kaynak × aksiyon izin matrisi.
 *
 * Uc yerde ayni bilesen kullaniliyor: rolun izin kumesini duzenlerken, kisiye
 * ozel izin verirken ve kisinin etkin iznini gosterirken. Ucu ayri ayri
 * yazilsaydi ayni izin uc ekranda uc turlu gorunurdu.
 *
 * Satirlar ve sutunlar sunucudan geliyor (GET /permissions/actions): modullerin
 * aksiyonlari ayni degil (uyelikte "degistirme" yok, kullanicilarda "uyelik
 * yonetimi" var), dolayisiyla sabit sutunlu gercek bir tablo cogu hucreyi bos
 * birakirdi.
 *
 * @param catalog  [{ module, permissions: ['ROLES.READ', ...] }]
 * @param value    secili izin anahtarlari
 * @param onChange yeni liste ile cagriliyor; verilmezse matris salt okunur
 * @param inherited rollerden gelen izinler — kisisel izin ekraninda "zaten
 *                  var" olarak isaretleniyor, tekrar verilmesin diye
 */
export default function PermissionMatrix({
  catalog,
  value = [],
  onChange,
  inherited = [],
  emptyText = 'İzin listesi okunamadı.',
}) {
  const readOnly = typeof onChange !== 'function';
  const selected = new Set(value);
  const fromRoles = new Set(inherited);

  if (!catalog || catalog.length === 0) {
    return <p className="st-empty">{emptyText}</p>;
  }

  const toggle = (key, on) => {
    onChange(on ? value.filter((k) => k !== key) : [...new Set([...value, key])]);
  };

  const toggleModule = (keys, allOn) => {
    onChange(
      allOn
        ? value.filter((k) => !keys.includes(k))
        : [...new Set([...value, ...keys])]
    );
  };

  return (
    <div className="st-perm-list" data-locked={readOnly}>
      {catalog.map(({ module, permissions: keys }) => {
        const on = keys.filter((k) => selected.has(k));
        const all = keys.length > 0 && on.length === keys.length;
        const some = on.length > 0;

        return (
          <div key={module} className="st-perm-module" data-on={some}>
            <div className="st-perm-module-head">
              <label>
                <input
                  type="checkbox"
                  checked={all}
                  disabled={readOnly}
                  // Kismi secim ucuncu bir durum: kutu isaretli degil ama
                  // "hicbiri" de degil. Isaretsiz gostermek yanlis bilgi verir.
                  ref={(el) => {
                    if (el) el.indeterminate = some && !all;
                  }}
                  onChange={() => toggleModule(keys, all)}
                />
                <strong>{moduleName(module)}</strong>
              </label>
              <small>
                {on.length}/{keys.length} izin
              </small>
            </div>

            <div className="st-perm-actions">
              {keys.map((key) => {
                const isOn = selected.has(key);
                const viaRole = fromRoles.has(key);

                return (
                  <label
                    key={key}
                    className="st-perm-switch"
                    data-on={isOn || viaRole}
                    data-inherited={viaRole && !isOn}
                    title={
                      viaRole && !isOn
                        ? 'Bu izin zaten bir rolden geliyor.'
                        : permissionHint(key) ?? key
                    }
                  >
                    <input
                      type="checkbox"
                      checked={isOn}
                      disabled={readOnly}
                      onChange={() => toggle(key, isOn)}
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
