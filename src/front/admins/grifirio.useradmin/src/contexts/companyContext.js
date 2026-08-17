import { createContext, useContext } from 'react';

/**
 * Panelin uzerinde calistigi sirket.
 *
 * Baglam nesnesi ve kancasi saglayici bilesenden ayri dosyada: ayni dosyada
 * hem bilesen hem sabit export etmek Fast Refresh'i bozuyor (eslint
 * react-refresh/only-export-components).
 */
export const CompanyContext = createContext(null);

export const useCompany = () => useContext(CompanyContext);

/** Secilen sirketin sekmeler/oturumlar arasi hatirlandigi anahtar. */
export const SELECTED_COMPANY_KEY = 'grafirio.selectedCompanyId';
