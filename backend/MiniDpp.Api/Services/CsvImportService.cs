using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MiniDpp.Api.Data;
using MiniDpp.Api.DTOs;
using MiniDpp.Api.Models;

namespace MiniDpp.Api.Services;

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
        using var reader = new StreamReader(fileStream);

        string? headerLine = await reader.ReadLineAsync();
        if (headerLine == null)
        {
            result.Errors.Add(new CsvRowError { RowNumber = 1, Reason = "CSV dosyasi bos." });
            return result;
        }

        // Baslik satiri 1. satir kabul edildigi icin veri satirlari 2'den baslar
        int currentRowNumber = 1;
        string? line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            currentRowNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            result.TotalRowsProcessed++;

            // CSV alanlarini virgulden ayir
            var columns = line.Split(',');
            if (columns.Length < 7)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = "Eksik sutun sayisi. Satir en az 7 sutun icermelidir."
                });
                continue;
            }

            string gtin = columns[0].Trim();
            string modelName = columns[1].Trim();
            string batchNo = columns[2].Trim();
            string cottonStr = columns[3].Trim();
            string elastaneStr = columns[4].Trim();
            string productionPlace = columns[5].Trim();
            string carbonStr = columns[6].Trim();

            // 1. GTIN ve GS1 Check Digit dogrulamasi
            var gtinValidation = GtinValidator.Validate(gtin);
            if (!gtinValidation.IsValid)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = gtinValidation.ErrorMessage!
                });
                continue;
            }

            // Mukerrer GTIN kontrolu
            bool gtinExists = await _context.ProductPassports.AnyAsync(p => p.Gtin == gtin);
            if (gtinExists)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = $"GTIN ({gtin}) sistemde zaten kayitli."
                });
                continue;
            }

            // 2. Metin alanlarinin bos olmama kontrolu
            if (string.IsNullOrWhiteSpace(modelName) || string.IsNullOrWhiteSpace(batchNo) || string.IsNullOrWhiteSpace(productionPlace))
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = "Model adi, batch numarasi ve uretim yeri alanlari bos birakilamaz."
                });
                continue;
            }

            // 3. Elyaf oranlari dogrulamasi
            if (!decimal.TryParse(cottonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cotton) ||
                !decimal.TryParse(elastaneStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal elastane))
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = "Elyaf pamuk ve elastan oranlari sayisal deger olmalidir."
                });
                continue;
            }

            if (cotton < 0 || cotton > 100 || elastane < 0 || elastane > 100)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = "Elyaf oranlari 0 ile 100 araliginda olmalidir."
                });
                continue;
            }

            if ((cotton + elastane) != 100m)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = $"Elyaf oranlari toplami %100 olmalidir. Mevcut toplam: %{cotton + elastane}"
                });
                continue;
            }

            // 4. Karbon degeri dogrulamasi
            if (!decimal.TryParse(carbonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal carbon) || carbon < 0)
            {
                result.FailedCount++;
                result.Errors.Add(new CsvRowError
                {
                    RowNumber = currentRowNumber,
                    Reason = "Karbon degeri sayisal ve negatif olmayan bir deger olmalidir."
                });
                continue;
            }

            // 5. Iliskisel veritabani kaydi (ProductModel -> Batch -> ProductPassport)
            var productModel = await _context.ProductModels.FirstOrDefaultAsync(m => m.Name == modelName);
            if (productModel == null)
            {
                productModel = new ProductModel { Name = modelName };
                _context.ProductModels.Add(productModel);
                await _context.SaveChangesAsync();
            }

            var batch = await _context.Batches.FirstOrDefaultAsync(b => b.BatchNo == batchNo);
            if (batch == null)
            {
                batch = new Batch
                {
                    BatchNo = batchNo,
                    ProductionPlace = productionPlace,
                    ProductModelId = productModel.Id
                };
                _context.Batches.Add(batch);
                await _context.SaveChangesAsync();
            }

            var passport = new ProductPassport
            {
                Gtin = gtin,
                CottonPercentage = cotton,
                ElastanePercentage = elastane,
                CarbonKgCo2e = carbon,
                BatchId = batch.Id
            };

            _context.ProductPassports.Add(passport);
            await _context.SaveChangesAsync();

            result.SuccessCount++;
            result.SavedGtins.Add(gtin);
        }

        return result;
    }
}