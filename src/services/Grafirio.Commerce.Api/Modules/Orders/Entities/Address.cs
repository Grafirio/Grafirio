namespace Grafirio.Commerce.Api.Modules.Orders;

public class Address
{
    public string Province { get; set; } = default!;
    public string District { get; set; } = default!;
    public string Street { get; set; } = default!;
    public string ZipCode { get; set; } = default!;
    public string Line { get; set; } = default!;
}
