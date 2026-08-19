/**
 * Ulkeye gore degisen yasal alan adlari.
 *
 * Ayni kutuya her yerde "VKN" yazmak yalnizca Turkiye'de dogru: ayni alan
 * Almanya'da USt-IdNr., ABD'de EIN, Birlesik Krallik'ta VAT number. Alanin
 * kendisi (Company.TaxId) tek; degisen sey etiketi, ipucu ve dogrulamasi.
 *
 * Profili olmayan ulkeler DEFAULT'a dusuyor — listede olmayan bir ulke
 * secildiginde alan kaybolmasin, yalnizca daha genel bir adla gorunsun.
 */

export const COUNTRIES = [
  { code: 'TR', name: 'Türkiye' },
  { code: 'DE', name: 'Almanya' },
  { code: 'US', name: 'Amerika Birleşik Devletleri' },
  { code: 'GB', name: 'Birleşik Krallık' },
  { code: 'FR', name: 'Fransa' },
  { code: 'NL', name: 'Hollanda' },
  { code: 'IT', name: 'İtalya' },
  { code: 'ES', name: 'İspanya' },
  { code: 'PL', name: 'Polonya' },
  { code: 'SE', name: 'İsveç' },
  { code: 'CH', name: 'İsviçre' },
  { code: 'AT', name: 'Avusturya' },
  { code: 'BE', name: 'Belçika' },
  { code: 'IE', name: 'İrlanda' },
  { code: 'AE', name: 'Birleşik Arap Emirlikleri' },
  { code: 'SA', name: 'Suudi Arabistan' },
  { code: 'QA', name: 'Katar' },
  { code: 'EG', name: 'Mısır' },
  { code: 'IN', name: 'Hindistan' },
  { code: 'CN', name: 'Çin' },
  { code: 'JP', name: 'Japonya' },
  { code: 'KR', name: 'Güney Kore' },
  { code: 'SG', name: 'Singapur' },
  { code: 'AU', name: 'Avustralya' },
  { code: 'CA', name: 'Kanada' },
  { code: 'BR', name: 'Brezilya' },
  { code: 'MX', name: 'Meksika' },
  { code: 'ZA', name: 'Güney Afrika' },
  { code: 'RU', name: 'Rusya' },
  { code: 'UA', name: 'Ukrayna' },
  { code: 'AZ', name: 'Azerbaycan' },
];


export const CURRENCIES = [
  { code: 'TRY', name: '₺ Türk lirası' },
  { code: 'USD', name: '$ ABD doları' },
  { code: 'EUR', name: '€ Euro' },
  { code: 'GBP', name: '£ Sterlin' },
  { code: 'CHF', name: 'CHF İsviçre frangı' },
  { code: 'AED', name: 'AED BAE dirhemi' },
  { code: 'SAR', name: 'SAR Suudi riyali' },
  { code: 'INR', name: '₹ Hindistan rupisi' },
  { code: 'CNY', name: '¥ Çin yuanı' },
  { code: 'JPY', name: '¥ Japon yeni' },
  { code: 'AUD', name: 'A$ Avustralya doları' },
  { code: 'CAD', name: 'C$ Kanada doları' },
  { code: 'BRL', name: 'R$ Brezilya reali' },
  { code: 'ZAR', name: 'R Güney Afrika randı' },
];

export const LOCALES = [
  { code: 'tr-TR', name: 'Türkçe (Türkiye)' },
  { code: 'en-US', name: 'English (United States)' },
  { code: 'en-GB', name: 'English (United Kingdom)' },
  { code: 'de-DE', name: 'Deutsch (Deutschland)' },
  { code: 'fr-FR', name: 'Français (France)' },
  { code: 'es-ES', name: 'Español (España)' },
  { code: 'ar-AE', name: 'العربية (الإمارات)' },
];

export const TIME_ZONES = [
  'Europe/Istanbul',
  'Europe/London',
  'Europe/Berlin',
  'Europe/Paris',
  'Europe/Amsterdam',
  'America/New_York',
  'America/Chicago',
  'America/Los_Angeles',
  'Asia/Dubai',
  'Asia/Riyadh',
  'Asia/Kolkata',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Australia/Sydney',
  'UTC',
];

export const INDUSTRY_SCHEMES = [
  { code: 'NACE', name: 'NACE (Avrupa Birliği)' },
  { code: 'NAICS', name: 'NAICS (Kuzey Amerika)' },
  { code: 'SIC', name: 'SIC (Birleşik Krallık)' },
  { code: 'ISIC', name: 'ISIC (Birleşmiş Milletler)' },
];

const GENERIC_LEGAL_FORMS = [
  { code: 'LTD', name: 'Limited şirket' },
  { code: 'CORP', name: 'Sermaye şirketi' },
  { code: 'PARTNERSHIP', name: 'Ortaklık' },
  { code: 'SOLE', name: 'Şahıs işletmesi' },
  { code: 'COOP', name: 'Kooperatif' },
  { code: 'NONPROFIT', name: 'Kâr amacı gütmeyen kuruluş' },
  { code: 'OTHER', name: 'Diğer' },
];

const DEFAULT_PROFILE = {
  taxIdLabel: 'Vergi / KDV numarası',
  taxIdHint: '',
  taxIdPattern: null,
  registrationLabel: 'Ticaret sicil numarası',
  secondaryRegistrationLabel: 'İkincil sicil numarası',
  taxOfficeLabel: 'Vergi dairesi',
  showTaxOffice: false,
  eInvoiceScheme: 'PEPPOL',
  eInvoiceLabel: 'E-fatura adresi',
  usesIban: true,
  legalForms: GENERIC_LEGAL_FORMS,
  defaultCurrency: 'USD',
  defaultLocale: 'en-US',
  defaultTimeZone: 'UTC',
};

/**
 * Ulke bazli profiller. Buradaki her giris, o ulkede bir muhasebecinin
 * tanidigi adi kullaniyor; "tax number" gibi cevirisi dogru ama pratikte
 * kimsenin aramadigi bir ad ise yaramiyor.
 */
const PROFILES = {
  TR: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'Vergi kimlik numarası (VKN)',
    taxIdHint: '10 haneli',
    taxIdPattern: /^\d{10}$/,
    registrationLabel: 'Ticaret sicil numarası',
    secondaryRegistrationLabel: 'MERSİS numarası',
    showTaxOffice: true,
    eInvoiceScheme: 'KEP',
    eInvoiceLabel: 'KEP adresi',
    legalForms: [
      { code: 'AS', name: 'Anonim Şirket (A.Ş.)' },
      { code: 'LTD', name: 'Limited Şirket (Ltd. Şti.)' },
      { code: 'SAHIS', name: 'Şahıs İşletmesi' },
      { code: 'KOLEKTIF', name: 'Kolektif Şirket' },
      { code: 'KOMANDIT', name: 'Komandit Şirket' },
      { code: 'KOOPERATIF', name: 'Kooperatif' },
      { code: 'OTHER', name: 'Diğer' },
    ],
    defaultCurrency: 'TRY',
    defaultLocale: 'tr-TR',
    defaultTimeZone: 'Europe/Istanbul',
  },
  DE: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'USt-IdNr. (KDV numarası)',
    taxIdHint: 'DE + 9 hane',
    taxIdPattern: /^DE\d{9}$/i,
    registrationLabel: 'Handelsregisternummer',
    secondaryRegistrationLabel: 'Steuernummer',
    legalForms: [
      { code: 'GMBH', name: 'GmbH' },
      { code: 'UG', name: 'UG (haftungsbeschränkt)' },
      { code: 'AG', name: 'AG' },
      { code: 'KG', name: 'KG' },
      { code: 'OHG', name: 'OHG' },
      { code: 'GBR', name: 'GbR' },
      { code: 'OTHER', name: 'Diğer' },
    ],
    defaultCurrency: 'EUR',
    defaultLocale: 'de-DE',
    defaultTimeZone: 'Europe/Berlin',
  },
  GB: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'VAT registration number',
    taxIdHint: 'GB + 9 hane',
    registrationLabel: 'Company number (Companies House)',
    secondaryRegistrationLabel: 'UTR numarası',
    eInvoiceScheme: 'PEPPOL',
    legalForms: [
      { code: 'LTD', name: 'Private limited company (Ltd)' },
      { code: 'PLC', name: 'Public limited company (PLC)' },
      { code: 'LLP', name: 'Limited liability partnership (LLP)' },
      { code: 'SOLE', name: 'Sole trader' },
      { code: 'OTHER', name: 'Diğer' },
    ],
    defaultCurrency: 'GBP',
    defaultLocale: 'en-GB',
    defaultTimeZone: 'Europe/London',
  },
  US: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'EIN (Federal vergi numarası)',
    taxIdHint: '9 haneli',
    taxIdPattern: /^\d{2}-?\d{7}$/,
    registrationLabel: 'State registration / file number',
    secondaryRegistrationLabel: 'State tax ID',
    // ABD IBAN kullanmiyor; hesap numarasi + ABA yonlendirme kodu ikilisi
    // olmadan musteri hesabini hic giremezdi.
    usesIban: false,
    eInvoiceScheme: '',
    eInvoiceLabel: 'E-fatura adresi',
    legalForms: [
      { code: 'INC', name: 'Corporation (Inc.)' },
      { code: 'LLC', name: 'Limited liability company (LLC)' },
      { code: 'LP', name: 'Limited partnership (LP)' },
      { code: 'SOLE', name: 'Sole proprietorship' },
      { code: 'NONPROFIT', name: '501(c) nonprofit' },
      { code: 'OTHER', name: 'Diğer' },
    ],
    defaultCurrency: 'USD',
    defaultLocale: 'en-US',
    defaultTimeZone: 'America/New_York',
  },
  FR: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'Numéro de TVA',
    registrationLabel: 'SIREN / SIRET',
    secondaryRegistrationLabel: 'RCS numarası',
    legalForms: [
      { code: 'SARL', name: 'SARL' },
      { code: 'SAS', name: 'SAS' },
      { code: 'SASU', name: 'SASU' },
      { code: 'SA', name: 'SA' },
      { code: 'EURL', name: 'EURL' },
      { code: 'OTHER', name: 'Diğer' },
    ],
    defaultCurrency: 'EUR',
    defaultLocale: 'fr-FR',
    defaultTimeZone: 'Europe/Paris',
  },
  IT: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'Partita IVA',
    registrationLabel: 'Numero REA',
    secondaryRegistrationLabel: 'Codice fiscale',
    eInvoiceScheme: 'PEC',
    eInvoiceLabel: 'PEC adresi',
    defaultCurrency: 'EUR',
    defaultTimeZone: 'Europe/Rome',
  },
  IN: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'GSTIN',
    taxIdHint: '15 haneli',
    registrationLabel: 'CIN (Corporate Identity Number)',
    secondaryRegistrationLabel: 'PAN',
    usesIban: false,
    defaultCurrency: 'INR',
    defaultTimeZone: 'Asia/Kolkata',
  },
  AE: {
    ...DEFAULT_PROFILE,
    taxIdLabel: 'TRN (Tax Registration Number)',
    registrationLabel: 'Ticaret lisansı numarası',
    secondaryRegistrationLabel: 'Serbest bölge kayıt no',
    defaultCurrency: 'AED',
    defaultTimeZone: 'Asia/Dubai',
  },
};

export const countryProfile = (code) => PROFILES[code] ?? DEFAULT_PROFILE;

/** IBAN: TR + 24 hane gibi, ulke kodu + 2 kontrol hanesi + 11-30 karakter. */
export const IBAN_RE = /^[A-Z]{2}\d{2}[A-Z0-9]{11,30}$/;
