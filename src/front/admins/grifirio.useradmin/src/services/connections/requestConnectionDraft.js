import axios from 'axios';
import withDesktopConnectionContext from './withDesktopConnectionContext.js';
import DesktopConnectionError from './DesktopConnectionError.js';
import isDesktopApp from '../../auth/desktop/isDesktopApp.js';
import { DATA_ANALYSIS_API_URL } from '../../constants/dataAnalysisApi.js';

const DRAFT_TIMEOUT_MS = 30000;
const BAD_REQUEST_STATUS = 400;
const UNPROCESSABLE_STATUS = 422;
const TARGET_CHANGED_MESSAGE = 'Bağlantı hedefi değişti. Test için veritabanı parolasını yeniden girin.';

// Our API marks user-facing connection test failures with success:false; these
// messages are already shown verbatim in the browser flow.
const readConnectionFailure = (response) =>
  (response?.status === BAD_REQUEST_STATUS || response?.status === UNPROCESSABLE_STATUS)
    && response.data?.success === false && typeof response.data.error === 'string' && response.data.error !== ''
    ? response.data.error
    : null;

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
      const connectionFailure = readConnectionFailure(error.response);
      if (connectionFailure !== null) throw new Error(connectionFailure);
      throw new DesktopConnectionError();
    }
    throw error;
  }
}