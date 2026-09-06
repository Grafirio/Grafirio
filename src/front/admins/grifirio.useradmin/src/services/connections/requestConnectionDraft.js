import axios from 'axios';
import withDesktopConnectionContext from './withDesktopConnectionContext.js';
import DesktopConnectionError from './DesktopConnectionError.js';
import isDesktopApp from '../../auth/desktop/isDesktopApp.js';
import { DATA_ANALYSIS_API_URL } from '../../constants/dataAnalysisApi.js';

const DRAFT_TIMEOUT_MS = 30000;
const BAD_REQUEST_STATUS = 400;
const TARGET_CHANGED_MESSAGE = 'Bağlantı hedefi değişti. Test için veritabanı parolasını yeniden girin.';

export default async function requestConnectionDraft(operation, connectionInfo) {
  const payload = await withDesktopConnectionContext(connectionInfo);
  try {
    const response = await axios.post(`${DATA_ANALYSIS_API_URL}/api/connections/${operation}`, payload, {
      timeout: DRAFT_TIMEOUT_MS,
    });
    if (isDesktopApp() && response.data?.success === false) throw new DesktopConnectionError();
    return response.data;
  } catch (error) {
    // An unavailable draft endpoint must never trigger a save or a direct probe.
    if (isDesktopApp()) {
      if (error.response?.status === BAD_REQUEST_STATUS
        && error.response.data?.error === TARGET_CHANGED_MESSAGE) {
        // Do not propagate Axios request credentials or arbitrary server details.
        throw new Error(TARGET_CHANGED_MESSAGE);
      }
      throw new DesktopConnectionError();
    }
    throw error;
  }
}