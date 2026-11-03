namespace MiniDpp.Api.Models;

public class ProductPassport
{
    public int Id { get; set; }

    // Baştaki sıfırları kaybetmemek için zorunlu olarak string
    public string Gtin { get; set; } = string.Empty;

    // Oranlar (%0 - %100 arası, toplamı %100 olmak zorunda)
    public decimal CottonPercentage { get; set; }
    public decimal ElastanePercentage { get; set; }

    // Karbon ayak izi - Yetkili olmayan rolde gizlenecek
    public decimal CarbonKgCo2e { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Foreign Key -> Hangi Batch'e ait olduğu
    public int BatchId { get; set; }
    public Batch Batch { get; set; } = null!;
}