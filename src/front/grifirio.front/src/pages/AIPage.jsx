import { useState, useEffect } from 'react';
import AIDashboard from '../components/AI/AIDashboard';
import { useKeycloak } from '@react-keycloak/web';

export default function AIPage() {
  const { keycloak } = useKeycloak();
  const [companyId, setCompanyId] = useState('1'); // Default company ID

  useEffect(() => {
    // Get company ID from Keycloak token or user profile
    if (keycloak?.tokenParsed) {
      // Assuming company_id is in the token
      const company = keycloak.tokenParsed.company_id || '1';
      setCompanyId(company);
    } else {
      // If no Keycloak, use default company ID (for development)
      setCompanyId('1');
    }
  }, [keycloak]);

  return (
    <div>
      <AIDashboard companyId={companyId} />
    </div>
  );
}
