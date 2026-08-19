using Grafirio.Identity.Api.Repositories;

namespace Grafirio.Identity.Api.Features.Companies;

/// <summary>
/// Bir müşteri tüzel kişiliği. Alt şirketler ayrı tüzel kişilik olduğu için
/// hiyerarşideki her kayıt kendi yasal kimliğini taşır; hiçbir alan üst
/// şirketten miras alınmaz.
///
/// Alanların hepsi nullable. Bunun tek nedeni teknik değil: MongoDB şemasız
/// olduğu için alan eklenmeden önce yazılmış belgelerde bu alanlar hiç yok ve
/// zorunlu bir tipe okumaya çalışmak "Document element is missing" ile bütün
/// kaydı okunamaz hale getiriyor. Daha önce tam olarak bu yaşandı.
/// </summary>
public class Company : BaseEntity
{
    // ── Kimlik ────────────────────────────────────────────────────────────
    //
    // Aşağıdaki "kilitli" alanlar bir kez doldurulduktan sonra değişmez;
    // kural <see cref="Update.UpdateCompanyCommandHandler"/> içinde, yani
    // sunucuda uygulanıyor. İstemcinin alanı salt-okunur çizmesi yalnızca
    // görsel bir engel olurdu — istek doğrudan da gönderilebilir.

    /// Panelde ve raporlarda görünen ad. Ticari ad yasal addan farklı
    /// olabildiği için bu alan kilitli değil.
    public string Name { get; set; } = string.Empty;

    /// Sicilde kayıtlı tam yasal ad. KİLİTLİ.
    public string? LegalName { get; set; }

    /// Kısa kod. KİLİTLİ — kayıtlar ve dış sistemler buna göre eşleşiyor.
    public string? Code { get; set; }

    /// ISO 3166-1 alpha-2. Hangi vergi/sicil alanlarının sorulacağını bu
    /// belirlediği için sonradan değişmesi bütün kimliği anlamsızlaştırır.
    /// KİLİTLİ.
    public string? CountryCode { get; set; }

    /// Tüzel yapı: AS, LTD, GMBH, INC, LLC, SARL… Ülkeye göre değiştiği için
    /// sabit bir enum yerine serbest kod tutuluyor. KİLİTLİ.
    public string? LegalForm { get; set; }

    /// Ticaret sicil numarası (TR: Ticaret Sicil No, DE: Handelsregister,
    /// GB: Company Number). KİLİTLİ.
    public string? RegistrationNumber { get; set; }

    /// Vergi kimliği (TR: VKN, US: EIN, AB: VAT No, IN: GSTIN). KİLİTLİ.
    public string? TaxId { get; set; }

    public DateTime? IncorporationDate { get; set; }

    // ── Kimlik: serbest ───────────────────────────────────────────────────

    /// Yalnızca bazı ülkelerde anlamlı (TR: bağlı vergi dairesi).
    public string? TaxOffice { get; set; }

    /// İkincil kayıt numarası: TR'de MERSİS, başka ülkelerde yerel karşılığı.
    public string? SecondaryRegistrationNumber { get; set; }

    /// ISO 17442 — küresel tüzel kişi tanımlayıcısı.
    public string? LeiCode { get; set; }

    /// Dun &amp; Bradstreet numarası.
    public string? DunsNumber { get; set; }

    /// Sektör sınıflandırma şeması: NACE, NAICS, SIC, ISIC.
    public string? IndustryScheme { get; set; }
    public string? IndustryCode { get; set; }
    public string? IndustryDescription { get; set; }

    public string? Description { get; set; }

    // ── Operasyonel varsayılanlar ─────────────────────────────────────────

    /// ISO 4217 (TRY, USD, EUR).
    public string? BaseCurrency { get; set; }

    /// BCP 47 (tr-TR, en-US).
    public string? Locale { get; set; }

    /// IANA saat dilimi (Europe/Istanbul). Windows kimliği değil: sunucular
    /// Linux ve IANA her iki tarafta da çözülebiliyor.
    public string? TimeZoneId { get; set; }

    /// Mali yılın başladığı ay (1–12). Ocak dışında başlayan ülkeler ve
    /// şirketler için raporlama dönemini belirler.
    public int? FiscalYearStartMonth { get; set; }

    /// Kayıt sihirbazından geliyor. Önceden <see cref="Description"/> içine
    /// "Ekip büyüklüğü: 6–20" diye yazılıyordu ve şirket açıklaması alanında
    /// kullanıcının karşısına o çıkıyordu.
    public string? TeamSize { get; set; }

    // ── İletişim ──────────────────────────────────────────────────────────

    public string? GeneralPhone { get; set; }
    public string? GeneralEmail { get; set; }
    public string? Website { get; set; }

    /// Elektronik fatura adresinin hangi ağa ait olduğu: KEP (TR), PEC (IT),
    /// PEPPOL (AB). Adresin kendisi tek başına hangi ağda geçerli olduğunu
    /// söylemediği için şema ayrı tutuluyor.
    public string? EInvoiceScheme { get; set; }
    public string? EInvoiceAddress { get; set; }

    // ── Temsilciler ve uyum ───────────────────────────────────────────────

    public string? AuthorizedSignatoryName { get; set; }
    public string? AuthorizedSignatoryTitle { get; set; }

    /// Veri koruma sorumlusu — GDPR md. 37 ve KVKK'nın irtibat kişisi aynı
    /// rolü tarif ettiği için tek alanla karşılanıyor.
    public string? DataProtectionOfficerName { get; set; }
    public string? DataProtectionOfficerEmail { get; set; }

    /// Yurt dışı temsilci — GDPR md. 27. Şirketin kurulu olmadığı bir
    /// yargı alanında zorunlu olduğu için ülkesi de tutuluyor.
    public string? PrivacyRepresentativeName { get; set; }
    public string? PrivacyRepresentativeEmail { get; set; }
    public string? PrivacyRepresentativeCountryCode { get; set; }

    // ── Listeler ──────────────────────────────────────────────────────────

    // Bos dizi olarak baslatiliyor, null olarak degil: EF sağlayıcısı
    // <c>OwnsMany</c> ile eslenmis bir diziyi belgede bulamazsa okumayi
    // tamamen kiriyor ("mapped collection but missing"). Yeni kayitlarin bu
    // duruma hic dusmemesi icin alan her zaman yaziliyor; eski kayitlari
    // <see cref="Repositories.CompanyEmbeddedListRepair"/> onariyor.
    public List<CompanyAddress>? Addresses { get; set; } = [];
    public List<CompanyBankAccount>? BankAccounts { get; set; } = [];

    // ── Hiyerarşi ve durum ────────────────────────────────────────────────

    public Guid? ParentCompanyId { get; set; }

    /// <summary>
    /// Kökten bu şirkete kadar olan kimlik zinciri: <c>[kökId, …, üstId, kendiId]</c>.
    ///
    /// Yetki hiyerarşik: bir şirketteki rol, o şirket ve tüm altları için geçerli.
    /// Bunu her sorguda ağacı tırmanarak hesaplamak yerine yol burada
    /// materyalize ediliyor; "erişebildiğim şirketler" tek bir dizi-kesişimi
    /// sorgusuna dönüşüyor. Alternatifi her yönetici × her alt şirket için ayrı
    /// üyelik satırı tutmaktı — yeni şube ya da yeni yönetici eklendikçe
    /// kaçınılmaz olarak kayan bir muhasebe.
    ///
    /// Şirket başka bir üstün altına taşınmadığı sürece hiç değişmiyor; taşıma
    /// diye bir işlem de yok.
    /// </summary>
    public List<Guid> Path { get; set; } = [];

    public int Level { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // ── SQL erişim onayı ──────────────────────────────────────────────────

    /// <summary>
    /// Müşteri, SQL bağlantısını "sa" gibi tam yetkili bir hesapla kaydettiğinde
    /// riskleri kabul ettiğine dair açık onayı. Onay firma seviyesinde tutulur;
    /// <see cref="Users.CompanyMembership"/> ile aynı denetim izi desenini izler,
    /// böylece onayı kimin ne zaman verdiği sonradan sorulabilir.
    ///
    /// Nullable olmasının nedeni yalnızca teknik değil: null "hiç sorulmadı"
    /// demek ve bu, alan eklenmeden önce yazılmış kayıtların gerçek durumu.
    /// </summary>
    public bool? SaAccessConsentGiven { get; set; }
    public DateTime? SaAccessConsentGivenAt { get; set; }
    public string? SaAccessConsentGivenBy { get; set; }

    /// Onay alınırken kullanıcıya gösterilen metnin sürümü — metin değişirse
    /// eski onayların neyi kapsadığı belirsiz kalmasın.
    public string? SaAccessConsentTextVersion { get; set; }
}

public static class CompanyAddressTypes
{
    /// Sicilde kayıtlı adres; tebligat buraya yapılır.
    public const string Registered = "REGISTERED";
    public const string Billing = "BILLING";
    public const string Operational = "OPERATIONAL";
    public const string Shipping = "SHIPPING";

    public static readonly string[] All = [Registered, Billing, Operational, Shipping];
}

public class CompanyAddress
{
    public string Type { get; set; } = CompanyAddressTypes.Registered;
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }

    /// İl / eyalet / bölge. Ülkeye göre adı değişse de yapı aynı.
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>
/// Banka hesabı. IBAN kullanmayan ülkeler (ABD, Kanada, Avustralya) hesap
/// numarası + yönlendirme kodu ikilisiyle çalıştığı için iki gösterim de
/// tutuluyor; yalnızca IBAN alanı olsaydı bu ülkelerdeki müşteri hesabını
/// hiç giremezdi.
/// </summary>
public class CompanyBankAccount
{
    public string? BankName { get; set; }
    public string? BranchName { get; set; }
    public string? AccountHolder { get; set; }
    public string? CountryCode { get; set; }

    /// ISO 4217 — aynı bankada farklı para birimlerinde hesap olabilir.
    public string? Currency { get; set; }

    public string? Iban { get; set; }
    public string? AccountNumber { get; set; }

    /// ABA (US), sort code (GB), BSB (AU) gibi yerel yönlendirme kodu.
    public string? RoutingCode { get; set; }

    /// SWIFT/BIC — uluslararası transferde IBAN'la birlikte gerekiyor.
    public string? SwiftBic { get; set; }

    public bool IsPrimary { get; set; }
}
