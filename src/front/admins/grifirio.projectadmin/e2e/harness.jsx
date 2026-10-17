import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import App from '../src/App.jsx';
import '../src/styles/theme.css';
import '../src/styles/projectadmin.css';
import '../src/index.css';

createRoot(document.getElementById('root')).render(<BrowserRouter><App /></BrowserRouter>);
