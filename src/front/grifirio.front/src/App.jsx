import React from 'react';
import { useRoutes } from 'react-router-dom';
import routes, { canvasRoutes } from './routes';

function App() {
  const element = useRoutes([...routes, ...canvasRoutes]);
  return element;
}

export default App;
