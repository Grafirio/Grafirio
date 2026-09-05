import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AnalysisContext } from './AnalysisContext';
import reconcileAnalysisState from '../utils/analysis/reconcileAnalysisState.js';
import selectAnalysisAnswers from '../utils/analysis/selectAnalysisAnswers.js';
import {
  startAnalysis, getAnalysisStatus, submitAnalysisAnswers,
} from '../services/dataAnalysisService';

/** Sunucudaki iş bitmemişse yenilemeden sonra takibi sürdürmek için. */
const STORAGE_KEY = 'grafirio.analysis.tracking';

const EMPTY = {
  open: false,
  minimized: false,
  connectionId: null,
  connectionName: '',
  running: false,
  status: null,
  questions: [],
  answers: {},
  summary: '',
  stats: null,
  consent: false,
  error: '',

  /* Takip bırakıldı ama iş sunucuda sürüyor olabilir.

     `running: false` tek başına yetmiyordu: `status` hâlâ 'analyzing'
     kalıyor ve ekranda "Analizi başlat" düğmesi geri geliyordu. Kullanıcı
     ona basınca aynı bağlantı için ikinci bir analiz kuyruğa giriyor —
     geniş bir şemada yirmi LLM çağrısı daha. "Sürüyor" deyip aynı anda
     "başlat" sunmak da kendi içinde çelişkili. */
  trackingAbandoned: false,
};

const POLL_INTERVAL_MS = 4000;

/* Takibin bırakıldığı süre.

   On dakikaydı ve artık yetmiyor: sözlük, şema büyükse parçalara bölünüp
   ayrı ayrı üretiliyor ve yirmi parçalık bir şemada iş rahatlıkla çeyrek
   saati buluyor. Bir de sunucu tarafındaki yeniden deneme varsa süre
   katlanıyor. Eski sınırda kullanıcı, arka planda BAŞARIYLA süren bir işe
   "tamamlanmadı" yazısı görüyordu.

   Bu bir zaman aşımı değil, yalnızca takibin bırakıldığı an: iş kuyrukta
   yürüyor ve biz izlemeyi bıraksak da bitiyor. Mesaj da bunu söylüyor. */
const POLL_TIMEOUT_MS = 45 * 60 * 1000;

export function AnalysisProvider({ children }) {
  const [analysis, setAnalysis] = useState(EMPTY);

  // Yoklama tek bir zamanlayicidan yurusun: modal acilip kapansa da,
  // kullanici sayfalar arasinda gezinse de ikinci bir dongu baslamamali.
  const timerRef = useRef(null);
  const stopPolling = useCallback(() => clearTimeout(timerRef.current), []);
  useEffect(() => stopPolling, [stopPolling]);

  const applyState = useCallback((state) => {
    setAnalysis(previous => reconcileAnalysisState(previous, state));

    if (state.status !== 'analyzing') sessionStorage.removeItem(STORAGE_KEY);
  }, []);

  const poll = useCallback((connectionId) => {
    const startedAt = Date.now();

    const tick = async () => {
      if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
        // İşi durduramıyoruz ve durdurmuyoruz da; bırakılan tek şey takip.
        // "Başarısız oldu" demek yanlış olurdu: analiz büyük ihtimalle
        // sürüyor ve bitince bağlantı hazır görünecek. Bu yüzden `error`
        // değil kendi durumu: hata kırmızı bir uyarı, bu ise bir bilgi.
        setAnalysis((p) => ({
          ...p, running: false, trackingAbandoned: true,
          open: true, minimized: false, error: '',
        }));
        sessionStorage.removeItem(STORAGE_KEY);
        return;
      }

      try {
        const state = await getAnalysisStatus(connectionId);
        if (state.status === 'analyzing') {
          // Sürerken de özet güncelleniyor: sunucu oraya "3/17 parça
          // tamamlandı" yazıyor. Önceden bu dal erken dönüyordu, yani
          // ilerleme üretilse bile ekrana hiç ulaşmıyordu.
          setAnalysis((p) => (
            p.summary === (state.summary || '') ? p : { ...p, summary: state.summary || '' }
          ));
          timerRef.current = setTimeout(tick, POLL_INTERVAL_MS);
          return;
        }
        applyState(state);
      } catch (error) {
        setAnalysis((p) => ({
          ...p, running: false, open: true, minimized: false,
          error: error.response?.data?.error || error.message,
        }));
        sessionStorage.removeItem(STORAGE_KEY);
      }
    };

    stopPolling();
    timerRef.current = setTimeout(tick, 3000);
  }, [applyState, stopPolling]);

  /* Tam sayfa yenilemesinden sonra takibi sürdür.
     İş sunucuda kuyrukta yürüdüğü için yenileme onu durdurmuyor; durdurulan
     tek şey takipti. Küçültülmüş bildirim yenilemeden sonra da geri geliyor. */
  useEffect(() => {
    const stored = sessionStorage.getItem(STORAGE_KEY);
    if (!stored) return;

    let cancelled = false;
    (async () => {
      try {
        const { connectionId, connectionName } = JSON.parse(stored);
        if (!connectionId) return;

        const state = await getAnalysisStatus(connectionId);
        if (cancelled) return;

        if (state.status === 'analyzing') {
          setAnalysis({
            ...EMPTY,
            open: true,
            minimized: true,
            connectionId,
            connectionName: connectionName || 'Bağlantı',
            running: true,
            status: state.status,
          });
          poll(connectionId);
        } else {
          sessionStorage.removeItem(STORAGE_KEY);
        }
      } catch {
        sessionStorage.removeItem(STORAGE_KEY);
      }
    })();

    return () => { cancelled = true; };
  }, [poll]);

  /** "Analiz Et" — modalı bir bağlantı için açar ve mevcut durumu okur. */
  const openFor = useCallback(async (connection) => {
    const connectionId = connection.savedConnectionId || connection.id;
    if (!connectionId) {
      setAnalysis({
        ...EMPTY, open: true, connectionName: connection.name || '',
        error: 'Bağlantı kimliği bulunamadı. Sayfayı yenileyip tekrar deneyin.',
      });
      return;
    }

    setAnalysis({
      ...EMPTY, open: true, connectionId, connectionName: connection.name || 'Bağlantı',
    });

    try {
      const state = await getAnalysisStatus(connectionId);
      applyState(state);
      // Sunucuda iş hâlâ sürüyorsa yoklamayı kaldığı yerden sürdür.
      if (state.status === 'analyzing') poll(connectionId);
    } catch (error) {
      setAnalysis((p) => ({ ...p, error: error.response?.data?.error || error.message }));
    }
  }, [applyState, poll]);

  /**
   * Bırakılan takibi kaldığı yerden sürdürür.
   *
   * İş kuyrukta yürüdüğü için yapılacak tek şey durumu yeniden okumak;
   * yeni bir analiz BAŞLATMIYOR. Kullanıcıya "sayfayı yenileyin" demenin
   * yerini alıyor.
   */
  const resumeTracking = useCallback(async () => {
    const { connectionId, connectionName } = analysis;
    if (!connectionId) return;

    // `running: true` burada görsel bir ayrıntı değil, kilit. Yalnızca
    // `trackingAbandoned`'ı false yapsaydık, ağ turu boyunca hem o false hem
    // `running` false hem `status` 'analyzing' kalırdı — yani "Analizi başlat"
    // düğmesi birkaç yüz milisaniyeliğine geri gelirdi. Az önce kapattığımız
    // kapının aynısı: kullanıcı o aralıkta basarsa süren işin üstüne ikinci
    // bir analiz kuyruğa girer.
    setAnalysis((p) => ({ ...p, running: true, trackingAbandoned: false, error: '' }));

    try {
      const state = await getAnalysisStatus(connectionId);
      applyState(state);

      if (state.status === 'analyzing') {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ connectionId, connectionName }));
        poll(connectionId);
      }
    } catch (error) {
      // Yenileme tutmadı: geldiğimiz duruma dönülüyor. Takip hâlâ bırakılmış
      // durumda ve "Durumu yenile" düğmesi geri geliyor ki tekrar denenebilsin.
      setAnalysis((p) => ({
        ...p,
        running: false,
        trackingAbandoned: true,
        error: error.response?.data?.error || error.message,
      }));
    }
  }, [analysis, applyState, poll]);

  const run = useCallback(async () => {
    const { connectionId, connectionName, consent } = analysis;
    // `answers` ve `summary` de sıfırlanıyor. Önceki turun cevapları duruyorsa
    // yeni turun soruları için gönderiliyorlardı: soru kimlikleri sunucuda her
    // sözlük üretiminde baştan (`q1`, `q2`, …) numaralandığı için çakışma
    // ihtimal değil, kesinlik. Eski özet de kalırsa yeni analiz sürerken
    // ilerleme satırının yerinde bir önceki analizin özeti görünüyordu.
    setAnalysis((p) => ({
      ...p, running: true, trackingAbandoned: false,
      error: '', questions: [], answers: {}, summary: '', stats: null,
    }));

    try {
      await startAnalysis(connectionId, consent);
    } catch (error) {
      setAnalysis((p) => ({
        ...p,
        running: false,
        error: error.response?.data?.detail || error.response?.data?.error || error.message,
      }));
      return;
    }

    sessionStorage.setItem(STORAGE_KEY, JSON.stringify({ connectionId, connectionName }));
    poll(connectionId);
  }, [analysis, poll]);

  const submit = useCallback(async () => {
    const answers = selectAnalysisAnswers(analysis.questions, analysis.answers);
    if (analysis.running || Object.keys(answers).length === 0) return;
    setAnalysis((p) => ({ ...p, running: true, error: '' }));
    try {
      const result = await submitAnalysisAnswers(analysis.connectionId, answers);
      // The POST may return only status; refresh to obtain the remaining questions.
      if (result.status) applyState({ ...result, questions: result.questions ?? analysis.questions });
      const state = await getAnalysisStatus(analysis.connectionId);
      applyState(state);
      if (state.status === 'analyzing') {
        sessionStorage.setItem(STORAGE_KEY, JSON.stringify({
          connectionId: analysis.connectionId, connectionName: analysis.connectionName,
        }));
        poll(analysis.connectionId);
      }
    } catch (error) {
      setAnalysis((p) => ({
        ...p, running: false,
        error: error.response?.data?.error || error.message,
      }));
    }
  }, [analysis, applyState, poll]);

  const minimize = useCallback(() => setAnalysis((p) => ({ ...p, minimized: true })), []);
  const restore = useCallback(() => setAnalysis((p) => ({ ...p, minimized: false })), []);

  /** Analiz sürerken kapatmak yalnızca takibi kaybettirir; küçültülür. */
  const dismiss = useCallback(() => setAnalysis((p) => (
    p.running ? { ...p, minimized: true, open: true } : { ...p, open: false, minimized: false }
  )), []);

  /** Bildirimi tamamen gizler; iş arka planda sürer, sorular gelince geri açılır. */
  const hide = useCallback(() => setAnalysis((p) => ({ ...p, open: false, minimized: false })), []);

  const setConsent = useCallback((consent) => setAnalysis((p) => ({ ...p, consent })), []);
  const setAnswer = useCallback((id, option) => setAnalysis((p) => ({
    ...p, answers: { ...p.answers, [id]: option },
  })), []);

  const value = useMemo(() => ({
    analysis, openFor, run, resumeTracking, submit,
    minimize, restore, dismiss, hide, setConsent, setAnswer,
  }), [analysis, openFor, run, resumeTracking, submit,
       minimize, restore, dismiss, hide, setConsent, setAnswer]);

  return <AnalysisContext.Provider value={value}>{children}</AnalysisContext.Provider>;
}
