namespace MiniDpp.Api.DTOs;

public class CsvRowError
{
    public int RowNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class CsvImportResultDto
{
    public int TotalRowsProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<CsvRowError> Errors { get; set; } = new();
    public List<string> SavedGtins { get; set; } = new();
}