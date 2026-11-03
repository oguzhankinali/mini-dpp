namespace MiniDpp.Api.Services;

public static class GtinValidator
{
    public static (bool IsValid, string? ErrorMessage) Validate(string? gtin)
    {
        if (string.IsNullOrWhiteSpace(gtin))
        {
            return (false, "GTIN bos olamaz.");
        }

        gtin = gtin.Trim();

        if (gtin.Length != 14)
        {
            return (false, $"GTIN 14 haneli olmalidir. Mevcut hane sayisi: {gtin.Length}");
        }

        if (!gtin.All(char.IsDigit))
        {
            return (false, "GTIN yalnizca rakamlardan olusmalidir.");
        }

        // GS1 GTIN-14 Check Digit algoritmasi
        // Ilk 13 basamak sagdan sola sirasiyla 3 ve 1 agirliklariyla carpilir.
        int checkDigit = gtin[13] - '0';
        int sum = 0;

        for (int i = 0; i < 13; i++)
        {
            int digit = gtin[i] - '0';
            int weight = (i % 2 == 0) ? 3 : 1;
            sum += digit * weight;
        }

        int calculatedCheck = (10 - (sum % 10)) % 10;

        if (checkDigit != calculatedCheck)
        {
            return (false, $"Gecersiz GS1 kontrol basamagi. Beklenen: {calculatedCheck}, Okunan: {checkDigit}");
        }

        return (true, null);
    }
}