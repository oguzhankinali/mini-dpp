using System.Text.RegularExpressions;

namespace MiniDpp.Api.Models;

public class ProductModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public ICollection<Batch> Batches { get; set; } = new List<Batch>();
}