namespace MiniDpp.Api.Models;

public class Batch
{
    public int Id { get; set; }

    public string BatchNo { get; set; } = string.Empty;

    public string ProductionPlace { get; set; } = string.Empty;

    public int ProductModelId { get; set; }
    public ProductModel ProductModel { get; set; } = null!;

    public ICollection<ProductPassport> Passports { get; set; } = new List<ProductPassport>();
}