using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using MiniDpp.Api.Data;
using MiniDpp.Api.DTOs;
using MiniDpp.Api.Models;

namespace MiniDpp.Api.Services;

public class CsvImportService
{
    private readonly AppDbContext _context;

    // InvariantCulture'da virgül binlik ayırıcı sayıldığı için sadece nokta ve eksi işaretine izin veriyoruz
    private const NumberStyles StrictDecimalStyle =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    // DB'deki decimal(8,2) sınırı
    private const decimal MaxCarbonValue = 999999.99m;

    private static readonly string[] RequiredHeaders =
    [
        "gtin",
        "model_adi",
        "batch_no",
        "elyaf_pamuk_yuzde",
        "elyaf_elastan_yuzde",
        "uretim_yeri",
        "karbon_kg_co2e"
    ];

    public CsvImportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CsvImportResultDto> ProcessCsvAsync(Stream fileStream)
    {
        var result = new CsvImportResultDto();

        // Dosya içi mükerrer kontrolü (sadece validasyondan geçip DB'ye yazılanlar eklenir)
        var processedGtinsInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool currentRowHasBadData = false;
        bool currentRowHasMissingField = false;

        using var reader = new StreamReader(fileStream);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            PrepareHeaderForMatch = args => NormalizeHeader(args.Header),
            HeaderValidated = null,
            BadDataFound = _ => currentRowHasBadData = true,
            MissingFieldFound = _ => currentRowHasMissingField = true
        };

        using var csv = new CsvReader(reader, config);

        try
        {
            if (!await csv.ReadAsync() || !csv.ReadHeader())
            {
                AddError(result, 1, "CSV dosyası boş veya başlık satırı okunamadı.");
                return result;
            }
        }
        catch (Exception)
        {
            AddError(result, 1, "CSV başlık satırı bozuk olduğu için dosya okunamadı.");
            return result;
        }

        if (currentRowHasBadData)
        {
            AddError(result, 1, "CSV başlık satırında hatalı tırnaklama veya biçim hatası bulundu.");
            return result;
        }

        var headers = csv.HeaderRecord;

        if (headers == null || headers.Length == 0)
        {
            AddError(result, 1, "CSV dosyasında geçerli bir başlık satırı bulunamadı.");
            return result;
        }

        var normalizedHeaders = headers.Select(NormalizeHeader).ToArray();

        if (normalizedHeaders.Distinct(StringComparer.Ordinal).Count() != normalizedHeaders.Length)
        {
            AddError(result, 1, "CSV dosyasında aynı isimde birden fazla sütun bulunamaz.");
            return result;
        }

        var missingHeaders = RequiredHeaders
            .Where(req => !normalizedHeaders.Contains(req, StringComparer.Ordinal))
            .ToList();

        if (missingHeaders.Count > 0)
        {
            AddError(result, 1, $"CSV dosyasında zorunlu sütunlar eksik: {string.Join(", ", missingHeaders)}");
            return result;
        }

        while (true)
        {
            currentRowHasBadData = false;
            currentRowHasMissingField = false;

            bool hasMoreRecords;

            try
            {
                hasMoreRecords = await csv.ReadAsync();
            }
            catch (Exception)
            {
                // Parser ilerleyemez durumda olabilir, sonsuz döngüye girmemek için okumayı kesiyoruz
                int failedRow = csv.Parser.Row > 0
                    ? csv.Parser.Row
                    : result.TotalRowsProcessed + 2;

                result.TotalRowsProcessed++;
                FailRow(result, failedRow, "CSV dosya yapısı bozuk olduğu için okuma bu satırda sonlandırıldı.");
                break;
            }

            if (!hasMoreRecords)
                break;

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

                if (currentRowHasBadData)
                {
                    FailRow(result, currentRowNumber, "Satırda geçersiz CSV tırnaklama veya biçim hatası bulundu.");
                    continue;
                }

                if (currentRowHasMissingField)
                {
                    FailRow(result, currentRowNumber, "Satırda eksik sütun tespit edildi. Tüm sütunlar eksiksiz olmalıdır.");
                    continue;
                }

                string cleanGtin = gtin?.Trim() ?? string.Empty;
                var gtinValidation = GtinValidator.Validate(cleanGtin);

                if (!gtinValidation.IsValid)
                {
                    FailRow(result, currentRowNumber, gtinValidation.ErrorMessage ?? "GTIN değeri geçersiz.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(modelName) ||
                    string.IsNullOrWhiteSpace(batchNo) ||
                    string.IsNullOrWhiteSpace(productionPlace))
                {
                    FailRow(result, currentRowNumber, "Model adı, batch numarası ve üretim yeri boş bırakılamaz.");
                    continue;
                }

                string cleanModelName = modelName.Trim();
                string cleanBatchNo = batchNo.Trim();
                string cleanProductionPlace = productionPlace.Trim();

                if (!decimal.TryParse(cottonStr, StrictDecimalStyle, CultureInfo.InvariantCulture, out decimal cotton) ||
                    !decimal.TryParse(elastaneStr, StrictDecimalStyle, CultureInfo.InvariantCulture, out decimal elastane))
                {
                    FailRow(result, currentRowNumber, "Elyaf oranları geçerli bir sayısal değer olmalıdır. Ondalık ayırıcı olarak nokta kullanın.");
                    continue;
                }

                // DB şeması decimal(5,2) olduğu için 2 basamaktan fazlasını yuvarlamaya bırakmadan reddet
                if (decimal.Round(cotton, 2) != cotton || decimal.Round(elastane, 2) != elastane)
                {
                    FailRow(result, currentRowNumber, "Elyaf oranları en fazla 2 ondalık basamak içerebilir.");
                    continue;
                }

                if (cotton < 0 || cotton > 100 || elastane < 0 || elastane > 100)
                {
                    FailRow(result, currentRowNumber, "Elyaf oranları 0 ile 100 arasında olmalıdır.");
                    continue;
                }

                if (cotton + elastane != 100m)
                {
                    FailRow(result, currentRowNumber, $"Elyaf oranları toplamı %100 olmalıdır. Mevcut toplam: %{cotton + elastane}");
                    continue;
                }

                if (!decimal.TryParse(carbonStr, StrictDecimalStyle, CultureInfo.InvariantCulture, out decimal carbon))
                {
                    FailRow(result, currentRowNumber, "Karbon değeri geçerli bir sayısal değer olmalıdır. Ondalık ayırıcı olarak nokta kullanın.");
                    continue;
                }

                if (carbon < 0)
                {
                    FailRow(result, currentRowNumber, "Karbon değeri negatif olamaz.");
                    continue;
                }

                if (decimal.Round(carbon, 2) != carbon)
                {
                    FailRow(result, currentRowNumber, "Karbon değeri en fazla 2 ondalık basamak içerebilir.");
                    continue;
                }

                if (carbon > MaxCarbonValue)
                {
                    FailRow(result, currentRowNumber, $"Karbon değeri {MaxCarbonValue} üst sınırını aşamaz.");
                    continue;
                }

                if (processedGtinsInFile.Contains(cleanGtin))
                {
                    FailRow(result, currentRowNumber, $"GTIN ({cleanGtin}) aynı dosyada daha önce başarıyla işlendi.");
                    continue;
                }

                bool gtinExistsInDb = await _context.ProductPassports
                    .AsNoTracking()
                    .AnyAsync(p => p.Gtin == cleanGtin);

                if (gtinExistsInDb)
                {
                    FailRow(result, currentRowNumber, $"GTIN ({cleanGtin}) sistemde zaten kayıtlı.");
                    continue;
                }

                var existingModel = await _context.ProductModels
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Name == cleanModelName);

                Batch? existingBatch = null;

                if (existingModel != null)
                {
                    existingBatch = await _context.Batches
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b =>
                            b.BatchNo == cleanBatchNo &&
                            b.ProductModelId == existingModel.Id);
                }

                if (existingBatch != null &&
                    !string.Equals(existingBatch.ProductionPlace, cleanProductionPlace, StringComparison.OrdinalIgnoreCase))
                {
                    FailRow(result, currentRowNumber, $"Parti ({cleanBatchNo}) bu model için daha önce farklı bir üretim yeri ({existingBatch.ProductionPlace}) ile kaydedilmiş.");
                    continue;
                }

                ProductModel? newModel = null;
                Batch? newBatch = null;

                if (existingBatch == null)
                {
                    if (existingModel == null)
                    {
                        newModel = new ProductModel { Name = cleanModelName };
                        _context.ProductModels.Add(newModel);
                    }

                    newBatch = new Batch
                    {
                        BatchNo = cleanBatchNo,
                        ProductionPlace = cleanProductionPlace
                    };

                    if (newModel != null)
                        newBatch.ProductModel = newModel;
                    else
                        newBatch.ProductModelId = existingModel!.Id;

                    _context.Batches.Add(newBatch);
                }

                var passport = new ProductPassport
                {
                    Gtin = cleanGtin,
                    CottonPercentage = cotton,
                    ElastanePercentage = elastane,
                    CarbonKgCo2e = carbon
                };

                if (existingBatch != null)
                    passport.BatchId = existingBatch.Id;
                else
                    passport.Batch = newBatch!;

                _context.ProductPassports.Add(passport);

                // Tüm grafiği tek seferde kaydet (EF Core dahili transaction'ı kullanır)
                await _context.SaveChangesAsync();

                processedGtinsInFile.Add(cleanGtin);
                result.SuccessCount++;
                result.SavedGtins.Add(cleanGtin);
            }
            catch (DbUpdateException)
            {
                FailRow(result, currentRowNumber, "Veritabanı tekillik (unique index) veya veri bütünlüğü kısıtlaması nedeniyle satır kaydedilemedi.");
            }
            catch (CsvHelperException)
            {
                FailRow(result, currentRowNumber, "CSV satırı okunurken veya ayrıştırılırken hata oluştu.");
            }
            catch (Exception)
            {
                FailRow(result, currentRowNumber, "Satır işlenirken beklenmeyen bir hata oluştu.");
            }
            finally
            {
                // Hatalı entity'lerin tracker'da asılı kalıp sonraki satırları bozmasını önle
                _context.ChangeTracker.Clear();
            }
        }

        return result;
    }

    private static string NormalizeHeader(string? header)
    {
        return header?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static void AddError(CsvImportResultDto result, int rowNumber, string reason)
    {
        result.Errors.Add(new CsvRowError
        {
            RowNumber = rowNumber,
            Reason = reason
        });
    }

    private static void FailRow(CsvImportResultDto result, int rowNumber, string reason)
    {
        result.FailedCount++;
        AddError(result, rowNumber, reason);
    }
}