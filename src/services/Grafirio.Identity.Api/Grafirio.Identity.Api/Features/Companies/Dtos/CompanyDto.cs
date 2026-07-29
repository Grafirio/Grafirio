namespace Grafirio.Identity.Api.Features.Companies.Dtos;

public class CompanyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public Guid? ParentCompanyId { get; set; }
    public int Level { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // "sa" gibi tam yetkili bir SQL hesabının kullanımına dair firma onayı.
    // null: henüz hiç sorulmamış.
    public bool? SaAccessConsentGiven { get; set; }
    public DateTime? SaAccessConsentGivenAt { get; set; }
    public string? SaAccessConsentGivenBy { get; set; }
    public string? SaAccessConsentTextVersion { get; set; }
}