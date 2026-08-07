import React from 'react';
import { useRoutes } from 'react-router-dom';
import routes, { canvasRoutes } from './routes';
import { AnalysisProvider } from './contexts/AnalysisProvider';
import AnalysisTracker from './components/Analysis/AnalysisTracker';

function App() {
  const element = useRoutes([...routes, ...canvasRoutes]);

  // AnalysisTracker rota agacinin disinda: analiz dakikalar suruyor ve is
  // sunucudaki kuyrukta yuruyor. Bildirim sayfa icinde dursaydi kullanici
  // baska bir ekrana gectigi anda hem bildirim hem yoklama kaybolurdu.
  return (
    <AnalysisProvider>
      {element}
      <AnalysisTracker />
    </AnalysisProvider>
  );
}

export default App;
