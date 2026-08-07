import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AnalysisContext } from './AnalysisContext';
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
};

const POLL_INTERVAL_MS = 4000;
const POLL_TIMEOUT_MS = 10 * 60 * 1000;

export function AnalysisProvider({ children }) {
  const [analysis, setAnalysis] = useState(EMPTY);

  // Yoklama tek bir zamanlayicidan yurusun: modal acilip kapansa da,
  // kullanici sayfalar arasinda gezinse de ikinci bir dongu baslamamali.
  const timerRef = useRef(null);
  const stopPolling = useCallback(() => clearTimeout(timerRef.current), []);
  useEffect(() => stopPolling, [stopPolling]);

  const applyState = useCallback((state) => {
    setAnalysis((p) => ({
      ...p,
      running: state.status === 'analyzing',
      status: state.status,
      questions: state.questions || [],
      summary: state.summary || '',
      stats: state.tableCount
        ? { tables: state.tableCount, columns: state.columnCount, sampled: state.sampledColumnCount }
        : p.stats,
      // Yanıtlanacak soru geldiyse küçültülmüş bildirim yetmez; modal geri
      // açılıyor. Hata da öyle: küçük bir rozette kaybolmamalı. Kullanıcı
      // bildirimi tamamen gizlemiş olsa bile geri açılıyor — aksi hâlde
      // sorular hiç sorulmadan analiz yarım kalırdı.
      open: (state.status === 'awaiting_answers' || state.status === 'failed') ? true : p.open,
      minimized: (state.status === 'awaiting_answers' || state.status === 'failed') ? false : p.minimized,
      error: state.status === 'failed'
        ? (state.summary || 'Analiz başarısız oldu. Sunucu loglarında sebebi yazıyor.')
        : '',
    }));

    if (state.status !== 'analyzing') sessionStorage.removeItem(STORAGE_KEY);
  }, []);

  const poll = useCallback((connectionId) => {
    const startedAt = Date.now();

    const tick = async () => {
      if (Date.now() - startedAt > POLL_TIMEOUT_MS) {
        setAnalysis((p) => ({
          ...p, running: false, open: true, minimized: false,
          error: 'Analiz 10 dakikada tamamlanmadı. Sunucu loglarını kontrol edin.',
        }));
        sessionStorage.removeItem(STORAGE_KEY);
        return;
      }

      try {
        const state = await getAnalysisStatus(connectionId);
        if (state.status === 'analyzing') {
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

  const run = useCallback(async () => {
    const { connectionId, connectionName, consent } = analysis;
    setAnalysis((p) => ({ ...p, running: true, error: '', questions: [], stats: null }));

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
    setAnalysis((p) => ({ ...p, running: true, error: '' }));
    try {
      await submitAnalysisAnswers(analysis.connectionId, analysis.answers);
      setAnalysis((p) => ({ ...p, running: false, status: 'ready' }));
    } catch (error) {
      setAnalysis((p) => ({
        ...p, running: false,
        error: error.response?.data?.error || error.message,
      }));
    }
  }, [analysis.connectionId, analysis.answers]);

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
    analysis, openFor, run, submit, minimize, restore, dismiss, hide, setConsent, setAnswer,
  }), [analysis, openFor, run, submit, minimize, restore, dismiss, hide, setConsent, setAnswer]);

  return <AnalysisContext.Provider value={value}>{children}</AnalysisContext.Provider>;
}
