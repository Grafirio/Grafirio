import { createContext, useContext } from 'react';

/**
 * Analiz takibi uygulama seviyesinde tutuluyor.
 *
 * Önceden durum SqlConnectionSettings'in içindeydi; kullanıcı başka bir
 * sayfaya geçince bileşen unmount oluyor, ilerleme bildirimi ve yoklama
 * onunla birlikte kayboluyordu. İş sunucudaki kuyrukta yürümeye devam
 * ediyordu ama kullanıcı bunu göremiyor, bittiğini de öğrenemiyordu.
 *
 * Sağlayıcı rota ağacının üstünde duruyor: rota değişimi durumu etkilemiyor.
 */
export const AnalysisContext = createContext(null);

export const useAnalysis = () => {
  const context = useContext(AnalysisContext);
  if (!context) {
    throw new Error('useAnalysis, AnalysisProvider içinde çağrılmalı.');
  }
  return context;
};
