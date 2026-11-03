using QRCoder;

namespace MiniDpp.Api.Services;

public class QrCodeService
{
    public byte[] GenerateQrCodePng(string payloadUrl)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payloadUrl, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);

        // 20 piksel modül boyutu ile net bir PNG çıktısı
        return qrCode.GetGraphic(20);
    }
}