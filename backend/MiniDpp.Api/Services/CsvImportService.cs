using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using MiniDpp.Api.Data;
using MiniDpp.Api.DTOs;
using MiniDpp.Api.Models;
using MiniDpp.Api.Services;

public class CsvImportService
{
    private readonly AppDbContext _context;

    public CsvImportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CsvImportResultDto> ProcessCsvAsync(Stream fileStream)
    {
        var result = new CsvImportResultDto();
        var processedGtinsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StreamReader(fileStream);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            PrepareHeaderForMatch = args => args.Header?.Trim().ToLowerInvariant() ?? string.Empty
        };

        using var csv = new CsvReader(reader, config);


        // Başlık satırını oku
        try
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader())
            {
                result.Errors.Add(new CsvRowError { RowNumber = 1, Reason = "CSV dosyasi bos veya baslik satiri okunamadi." });
                return result;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(new CsvRowError { RowNumber = 1, Reason = $"CSV basligi okunurken hata olustu: {ex.Message}" });
            return result;
        }

        var headers = csv.HeaderRecord;
        var requiredHeaders = new[] { "gtin", "model_adi", "batch_no", "elyaf_pamuk_yuzde", "elyaf_elastan_yuzde", "uretim_yeri", "karbon_kg_co2e" };
        if (headers == null || !requiredHeaders.All(h => headers.Select(x => x.Trim().ToLowerInvariant()).Contains(h)))
        {
            result.Errors.Add(new CsvRowError
            {
                RowNumber = 1,
                Reason = "Gecersiz CSV basliklari. Dosya su basliklari icermelidir: gtin, model_adi, batch_no, elyaf_pamuk_yuzde, elyaf_elastan_yuzde, uretim_yeri, karbon_kg_co2e"
            });
            return result;
        }

        // Satırları tek tek işle; bir satır patlasa bile diğerleri devam eder
        while (true)
        {
            bool hasMoreRecords;
            try
            {
                hasMoreRecords = await csv.ReadAsync();
            }
            catch (Exception ex)
            {
                result.TotalRowsProcessed++;
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = csv.Parser.Row,
                    Reason = $"Bozuk satir formati veya CSV okuma hatasi: {ex.Message}"
                });
                continue;
            }

            if (!hasMoreRecords)
            {
                break;
            }

            int currentRowNumber = csv.Parser.Row;
            result.TotalRowsProcessed++;

            try
            {
                string? gtin = csv.GetField("gtin");
                string? modelName = csv.GetField("model_adi");
                string? batchNo = csv.GetField("batch_no");
                string? cottonStr = csv.GetField("elyaf_pamuk_yuzde");
                string? elastaneStr = csv.GetField("elyaf_elastan_yuzde");
                string? productionPlace = csv.GetField("uretim_yeri");
                string? carbonStr = csv.GetField("karbon_kg_co2e");

                // 1. GTIN ve GS1 kontrolü
                var gtinValidation = GtinValidator.Validate(gtin);
                if (!gtinValidation.IsValid)
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = gtinValidation.ErrorMessage! });
                    continue;
                }

                string validGtin = gtin!.Trim();

                // Dosya içi mükerrer GTIN kontrolü
                if (!processedGtinsInFile.Add(validGtin))
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = $"GTIN ({validGtin}) ayni CSV dosyasinda birden fazla kez yer aliyor." });
                    continue;
                }

                // Veritabanı mükerrer GTIN kontrolü
                bool gtinExistsInDb = await _context.ProductPassports.AnyAsync(p => p.Gtin == validGtin);
                if (gtinExistsInDb)
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = $"GTIN ({validGtin}) sistemde zaten kayitli." });
                    continue;
                }

                // 2. Zorunlu alan kontrolü
                if (string.IsNullOrWhiteSpace(modelName) || string.IsNullOrWhiteSpace(batchNo) || string.IsNullOrWhiteSpace(productionPlace))
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = "Model adi, batch numarasi ve uretim yeri bos birakilamaz." });
                    continue;
                }

                // 3. Elyaf oranları kontrolü
                if (!decimal.TryParse(cottonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cotton) ||
                    !decimal.TryParse(elastaneStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal elastane))
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = "Elyaf oranlari sayisal deger olmalidir." });
                    continue;
                }

                if (cotton < 0 || cotton > 100 || elastane < 0 || elastane > 100)
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = "Elyaf oranlari 0 ile 100 araliginda olmalidir." });
                    continue;
                }

                if ((cotton + elastane) != 100m)
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = $"Elyaf oranlari toplami %100 olmalidir. Mevcut toplam: %{cotton + elastane}" });
                    continue;
                }

                // 4. Karbon değeri kontrolü
                if (!decimal.TryParse(carbonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal carbon) || carbon < 0)
                {
                    result.FailedCount++;
                    result.Errors.Add(new CsvRowError { RowNumber = currentRowNumber, Reason = "Karbon degeri sayisal ve negatif olmayan bir deger olmalidir." });
                    continue;
                }

                // 5. Model ve Modele bağlı Parti Eşleştirmesi
                var productModel = await _context.ProductModels.FirstOrDefaultAsync(m => m.Name == modelName.Trim());
                if (productModel == null)
                {
                    productModel = new ProductModel { Name = modelName.Trim() };
                    _context.ProductModels.Add(productModel);
                    await _context.SaveChangesAsync();
                }

                var batch = await _context.Batches.FirstOrDefaultAsync(b => b.BatchNo == batchNo.Trim() && b.ProductModelId == productModel.Id);
                if (batch == null)
                {
                    batch = new Batch
                    {
                        BatchNo = batchNo.Trim(),
                        ProductionPlace = productionPlace.Trim(),
                        ProductModelId = productModel.Id
                    };
                    _context.Batches.Add(batch);
                    await _context.SaveChangesAsync();
                }

                var passport = new ProductPassport
                {
                    Gtin = validGtin,
                    CottonPercentage = cotton,
                    ElastanePercentage = elastane,
                    CarbonKgCo2e = carbon,
                    BatchId = batch.Id
                };

                _context.ProductPassports.Add(passport);
                await _context.SaveChangesAsync();

                result.SuccessCount++;
                result.SavedGtins.Add(validGtin);
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = $"Satir islenirken beklenmeyen hata olustu: {ex.Message}"
                });
            }
        }

        return result;
    }
}