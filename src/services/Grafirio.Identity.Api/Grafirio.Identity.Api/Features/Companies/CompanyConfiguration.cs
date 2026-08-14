using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Grafirio.Identity.Api.Features.Companies;

/// <summary>
/// Adres ve banka hesabı listeleri, şirket belgesinin içine gömülü diziler.
/// MongoDB sağlayıcısı bunları kendiliğinden gömülü saymıyor; ayrı bir varlık
/// sanıp kendi koleksiyonunu arıyor ve model kurulurken hata veriyor. Bu iki
/// satır olmadan hata derlemede değil, uygulama açılırken çıkıyor —
/// <c>OwnsMany</c> ilişkinin sahiplik olduğunu açıkça söylüyor.
/// </summary>
public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.OwnsMany(x => x.Addresses);
        builder.OwnsMany(x => x.BankAccounts);
    }
}
