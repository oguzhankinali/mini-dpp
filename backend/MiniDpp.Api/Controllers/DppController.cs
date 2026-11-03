using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniDpp.Api.Data;
using MiniDpp.Api.DTOs;
using MiniDpp.Api.Services;

namespace MiniDpp.Api.Controllers;

[ApiController]
public class DppController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CsvImportService _csvImportService;
    private readonly QrCodeService _qrCodeService;

    public DppController(
        AppDbContext context,
        CsvImportService csvImportService,
        QrCodeService qrCodeService)
    {
        _context = context;
        _csvImportService = csvImportService;
        _qrCodeService = qrCodeService;
    }

    // 1. CSV Yukleme Endpoint'i
    [HttpPost("api/dpp/import-csv")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CsvImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportCsv(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Lutfen gecerli bir CSV dosyasi secin." });
        }

        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Yalnizca .csv uzantili dosyalar yuklenebilir." });
        }

        using var stream = file.OpenReadStream();
        var result = await _csvImportService.ProcessCsvAsync(stream);

        return Ok(result);
    }

    // Gecerli tum urunleri listeleyen yardimci endpoint (Frontend icin)
    [HttpGet("api/dpp/products")]
    public async Task<IActionResult> GetAllProducts()
    {
        var products = await _context.ProductPassports
            .Include(p => p.Batch)
                .ThenInclude(b => b.ProductModel)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new
            {
                p.Gtin,
                ModelName = p.Batch.ProductModel.Name,
                p.Batch.BatchNo,
                p.Batch.ProductionPlace,
                p.CottonPercentage,
                p.ElastanePercentage,
                p.CarbonKgCo2e,
                p.CreatedAt
            })
            .ToListAsync();

        return Ok(products);
    }

    // 3. HTML Urun Pasaportu Ciktisi
    [HttpGet("dpp/{gtin}")]
    [Produces("text/html")]
    public async Task<IActionResult> GetHtmlPassport([FromRoute] string gtin, [FromQuery] string? rol)
    {
        var passport = await _context.ProductPassports
            .Include(p => p.Batch)
                .ThenInclude(b => b.ProductModel)
            .FirstOrDefaultAsync(p => p.Gtin == gtin);

        if (passport == null)
        {
            return NotFound($"<html><body><h2>Urun Pasaportu Bulunamadi</h2><p>GTIN: {HtmlEncoder.Default.Encode(gtin)}</p></body></html>");
        }

        bool isAuthorized = string.Equals(rol?.Trim(), "yetkili", StringComparison.OrdinalIgnoreCase);
        string currentHost = $"{Request.Scheme}://{Request.Host}";
        string qrUrl = $"{currentHost}/dpp/{passport.Gtin}/qr";
        string jsonLdUrl = $"{currentHost}/dpp/{passport.Gtin}.jsonld{(isAuthorized ? "?rol=yetkili" : string.Empty)}";

        // Yetkili rolunde karbon degeri gosterilir, diger durumlarda gizlenir
        string carbonSection = isAuthorized
            ? $@"<div class='carbon-box'>
                    <strong>Karbon Ayak Izi:</strong> {passport.CarbonKgCo2e:F2} kg CO2e
                    <span class='badge'>Yetkili Erisimi</span>
                 </div>"
            : string.Empty;

        string htmlContent = $@"<!DOCTYPE html>
<html lang='tr'>
<head>
    <meta charset='utf-8' />
    <meta name='viewport' content='width=device-width, initial-scale=1.0' />
    <title>{HtmlEncoder.Default.Encode(passport.Batch.ProductModel.Name)} - Dijital Urun Pasaportu</title>
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #f4f6f8; margin: 0; padding: 24px; color: #1e293b; }}
        .card {{ max-width: 580px; margin: 0 auto; background: #ffffff; border-radius: 12px; box-shadow: 0 4px 14px rgba(0,0,0,0.08); overflow: hidden; border: 1px solid #e2e8f0; }}
        .header {{ background: #0f172a; color: #ffffff; padding: 24px; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 20px; letter-spacing: 0.5px; }}
        .header p {{ margin: 6px 0 0 0; color: #94a3b8; font-size: 13px; }}
        .content {{ padding: 24px; }}
        .item {{ display: flex; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid #f1f5f9; font-size: 14px; }}
        .item:last-child {{ border-bottom: none; }}
        .label {{ color: #64748b; font-weight: 500; }}
        .value {{ font-weight: 600; color: #0f172a; }}
        .carbon-box {{ margin-top: 18px; padding: 14px; background: #ecfdf5; border: 1px solid #a7f3d0; border-radius: 8px; color: #065f46; font-size: 14px; display: flex; justify-content: space-between; align-items: center; }}
        .badge {{ background: #10b981; color: white; padding: 3px 8px; border-radius: 4px; font-size: 11px; font-weight: 600; text-transform: uppercase; }}
        .qr-section {{ text-align: center; margin-top: 24px; padding-top: 20px; border-top: 1px solid #f1f5f9; }}
        .qr-section img {{ width: 150px; height: 150px; border-radius: 6px; }}
        .footer-links {{ margin-top: 16px; text-align: center; font-size: 13px; }}
        .footer-links a {{ color: #2563eb; text-decoration: none; margin: 0 8px; }}
        .footer-links a:hover {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='card'>
        <div class='header'>
            <h1>Dijital Urun Pasaportu (DPP)</h1>
            <p>ESG Next Surdurulebilirlik Takip Sistemi</p>
        </div>
        <div class='content'>
            <div class='item'><span class='label'>Model Adi:</span><span class='value'>{HtmlEncoder.Default.Encode(passport.Batch.ProductModel.Name)}</span></div>
            <div class='item'><span class='label'>GTIN:</span><span class='value'>{passport.Gtin}</span></div>
            <div class='item'><span class='label'>Parti / Batch No:</span><span class='value'>{HtmlEncoder.Default.Encode(passport.Batch.BatchNo)}</span></div>
            <div class='item'><span class='label'>Uretim Yeri:</span><span class='value'>{HtmlEncoder.Default.Encode(passport.Batch.ProductionPlace)}</span></div>
            <div class='item'><span class='label'>Pamuk Orani:</span><span class='value'>%{passport.CottonPercentage:G29}</span></div>
            <div class='item'><span class='label'>Elastan Orani:</span><span class='value'>%{passport.ElastanePercentage:G29}</span></div>
            {carbonSection}
            <div class='qr-section'>
                <img src='{qrUrl}' alt='DPP QR Kodu' />
                <p style='color: #64748b; font-size: 12px; margin-top: 8px;'>Dogrulama QR Kodu</p>
            </div>
            <div class='footer-links'>
                <a href='{jsonLdUrl}' target='_blank'>JSON-LD Gorunumu</a>
                {(isAuthorized ? "<a href='?'>Tuketici Gorunumune Gec</a>" : "<a href='?rol=yetkili'>Yetkili Olarak Gor</a>")}
            </div>
        </div>
    </div>
</body>
</html>";

        return Content(htmlContent, "text/html; charset=utf-8");
    }

    // 4. JSON-LD Ciktisi (schema.org/Product)
    [HttpGet("dpp/{gtin}.jsonld")]
    [Produces("application/ld+json")]
    public async Task<IActionResult> GetJsonLdPassport([FromRoute] string gtin, [FromQuery] string? rol)
    {
        var passport = await _context.ProductPassports
            .Include(p => p.Batch)
                .ThenInclude(b => b.ProductModel)
            .FirstOrDefaultAsync(p => p.Gtin == gtin);

        if (passport == null)
        {
            return NotFound(new { error = "Urun pasaportu bulunamadi." });
        }

        bool isAuthorized = string.Equals(rol?.Trim(), "yetkili", StringComparison.OrdinalIgnoreCase);

        var jsonLdData = new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = passport.Batch.ProductModel.Name,
            ["gtin14"] = passport.Gtin,
            ["productionBatch"] = passport.Batch.BatchNo,
            ["countryOfAssembly"] = passport.Batch.ProductionPlace,
            ["material"] = new[]
            {
                new { name = "Cotton", percentage = $"{passport.CottonPercentage:G29}%" },
                new { name = "Elastane", percentage = $"{passport.ElastanePercentage:G29}%" }
            }
        };

        // Yalnizca yetkili rolunde karbon degeri schema.org propertyValue olarak eklenir
        if (isAuthorized)
        {
            jsonLdData["additionalProperty"] = new[]
            {
                new
                {
                    @type = "PropertyValue",
                    name = "CarbonFootprint",
                    value = passport.CarbonKgCo2e,
                    unitText = "kg CO2e"
                }
            };
        }

        var jsonString = JsonSerializer.Serialize(jsonLdData, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return Content(jsonString, "application/ld+json; charset=utf-8");
    }

    // 5. QR Kod PNG Ciktisi
    [HttpGet("dpp/{gtin}/qr")]
    [Produces("image/png")]
    public async Task<IActionResult> GetQrCode([FromRoute] string gtin)
    {
        bool exists = await _context.ProductPassports.AnyAsync(p => p.Gtin == gtin);
        if (!exists)
        {
            return NotFound();
        }

        // QR kodun yonlendirecegi HTML DPP linki
        string redirectUrl = $"{Request.Scheme}://{Request.Host}/dpp/{gtin}";
        byte[] qrBytes = _qrCodeService.GenerateQrCodePng(redirectUrl);

        return File(qrBytes, "image/png");
    }
}