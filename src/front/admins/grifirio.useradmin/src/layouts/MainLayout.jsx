import Nav from '../components/organisms/Nav';
import '../styles/MainLayout.css';

/**
 * Panel kabugu: yapiskan ust menu, icerik ve alt serit.
 *
 * Onceki surum icerigi Tabler'in .page-body > .container-xl sarmalina
 * koyuyordu; sayfalarin kendi genislik ve bosluklari zaten oldugu icin bu
 * ikinci bir kenar bosluu ekliyor ve tasarimdaki 1280px'lik hizayi
 * kaydiriyordu. Genislik artik sayfanin kendi isi.
 *
 * Tasarimdaki alt seritte ayrica "Yardim merkezi / Gizlilik / Destek talebi"
 * baglantilari var; arkalarinda sayfa olmadigi icin konmadi — hicbir yere
 * gitmeyen baglanti, olmayan baglantidan kotu.
 */
const MainLayout = ({ children }) => {
  return (
    <div className="shell">
      <Nav />
      <main className="shell-main">{children}</main>
      <footer className="shell-foot">
        <div className="shell-foot-inner">
          <span className="shell-foot-mark">Grafirio · Yönetim Paneli</span>
        </div>
      </footer>
    </div>
  );
};

export default MainLayout;
