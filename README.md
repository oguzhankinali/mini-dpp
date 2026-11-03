# Mini Dijital Ürün Pasaportu (Mini DPP)

Bu proje, **ESG Next | TÜBİTAK 1507 Destekli Ar-Ge Projesi** kapsamında Yazılım Mühendisi (Full-Stack) teknik değerlendirmesi amacıyla geliştirilmiş bir Dijital Ürün Pasaportu (DPP) prototipidir.

Uygulama; tekstil ürünlerine ait CSV verilerini GS1 ve içerik kurallarına göre doğrulayan, ilişkisel veritabanında saklayan, tüketici ve yetkili rolleri için dinamik HTML pasaportu, makine tarafından okunabilir JSON-LD (schema.org/Product) ve yönlendirilebilir QR kod çıktıları sunan bir ASP.NET Core Web API ile React + TypeScript yönetim panelinden oluşur.

---

## Mimari ve Kullanılan Teknolojiler

- **Backend:** ASP.NET Core Web API (.NET 10 / C#)
- **Veritabanı & ORM:** SQLite, Entity Framework Core (Code-First)
- **Frontend:** React 19, TypeScript, Vite, Lucide Icons
- **Doğrulama & Standartlar:** GS1 GTIN-14 Modulo 10 Check Digit Algoritması, W3C JSON-LD (schema.org/Product)
- **QR Kod Üretimi:** QRCoder kütüphanesi (PNG stream)

---

## Veri Modeli ve İlişki Yapısı

Şartname gereği model ve parti/lot ayrımı ilişkisel yapıda kurgulanmıştır:

```text
[ProductModel] (1) <---> (N) [Batch] (1) <---> (N) [ProductPassport]
```

- **ProductModel:** Tasarım ve model adını tutar (`Id`, `Name`).
- **Batch:** Üretim partisi ve lokasyon bilgilerini tutar (`Id`, `BatchNo`, `ProductionPlace`, `ProductModelId`).
- **ProductPassport:** GTIN bazlı tekil ürün pasaportudur (`Id`, `Gtin` [Unique string], `CottonPercentage`, `ElastanePercentage`, `CarbonKgCo2e`, `BatchId`, `CreatedAt`).

---

## Kurulum ve Çalıştırma

### Gereksinimler
- .NET 9 veya .NET 10 SDK
- Node.js (v18+) ve npm

### 1. Backend'i Başlatma

Terminalde proje kök dizinindeyken:

```bash
cd backend/MiniDpp.Api
dotnet run
```

Backend servisleri dinlemeye başladığında:
- **DPP API Sunucusu:** http://localhost:5059
- **Swagger Dokümantasyonu:** http://localhost:5059/swagger
- *(Not: `dpp.db` SQLite veritabanı dosyası ilk çalıştırmada otomatik olarak oluşturulur.)*

### 2. Frontend'i Başlatma

Ayrı bir terminal penceresinde:

```bash
cd frontend
npm install --legacy-peer-deps
npm run dev
```

- **Yönetim Paneli:** http://localhost:5173

---

## Veri Doğrulama Kuralları

CSV içe aktarma işleminde (`POST /api/dpp/import-csv`) her satır şu adımlardan geçer:
1. **GTIN Doğrulaması:** 14 haneli sayısal metin olmalı ve GS1 Modulo 10 sağdan sola ağırlıklı (3x, 1x) kontrol basamağı kuralını sağlamalıdır.
2. **Elyaf Oranı:** Pamuk ve elastan oranları 0-100 arasında olmalı ve toplamları kesin olarak %100 etmelidir.
3. **Zorunlu Alanlar:** Model adı, parti numarası ve üretim yeri boş bırakılamaz.
4. **Karbon Ayak İzi:** Sayısal ve negatif olmayan (>= 0) bir değer olmalıdır.
5. **Kısmi Başarı Desteği:** Hatalı satırlar atlanarak hata nedeni ve CSV satır numarası raporlanır; geçerli satırların veritabanına kaydı kesintisiz devam eder.

---

## Örnek URL'ler ve Çıktılar

Örnek ürün GTIN: `08690000000012`

| İşlev | URL | Açıklama |
|---|---|---|
| **Tüketici HTML DPP** | http://localhost:5059/dpp/08690000000012 | Varsayılan görünüm; karbon ayak izi gizlidir. |
| **Yetkili HTML DPP** | http://localhost:5059/dpp/08690000000012?rol=yetkili | Karbon değeri (12.40 kg CO2e) ve yetkili rozeti gösterilir. |
| **JSON-LD Çıktısı** | http://localhost:5059/dpp/08690000000012.jsonld | schema.org/Product formatında makine okunabilir veri. |
| **Yetkili JSON-LD** | http://localhost:5059/dpp/08690000000012.jsonld?rol=yetkili | additionalProperty altında karbon verisini içerir. |
| **QR Kod (PNG)** | http://localhost:5059/dpp/08690000000012/qr | İlgili HTML DPP adresine yönlendiren PNG görseli. |

---

## Tasarım ve Mimari Kararları

- **GTIN Formatı:** GTIN verileri baştaki sıfırların korunması için veritabanında ve tüm servis katmanlarında `string` tipinde saklanmıştır.
- **Server-Side Rendered HTML Pasaportu:** `/dpp/{gtin}` ucu, son tüketicinin ve QR kod okuyucularının herhangi bir istemci kütüphanesine bağımlı kalmadan doğrudan erişebilmesi amacıyla sunucu taraflı `text/html` formatında render edilmektedir.
- **Kültür Bağımsız Sayı Ayrıştırma:** Ondalıklı değerlerin okunmasında nokta/virgül karmaşasını engellemek için `CultureInfo.InvariantCulture` kullanılmıştır.