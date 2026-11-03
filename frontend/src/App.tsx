import React, { useState, useEffect } from 'react';
import { Upload, CheckCircle2, AlertTriangle, FileText, QrCode, ShieldCheck, Eye, RefreshCw } from 'lucide-react';

interface CsvRowError {
  rowNumber: number;
  reason: string;
}

interface CsvImportResult {
  totalRowsProcessed: number;
  successCount: number;
  failedCount: number;
  errors: CsvRowError[];
  savedGtins: string[];
}

interface ProductItem {
  gtin: string;
  modelName: string;
  batchNo: string;
  productionPlace: string;
  cottonPercentage: number;
  elastanePercentage: number;
  carbonKgCo2e: number;
  createdAt: string;
}

const API_BASE_URL = 'http://localhost:5059';

export default function App() {
  const [file, setFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);
  const [importResult, setImportResult] = useState<CsvImportResult | null>(null);
  const [products, setProducts] = useState<ProductItem[]>([]);
  const [selectedQrGtin, setSelectedQrGtin] = useState<string | null>(null);

  const fetchProducts = async () => {
    try {
      const res = await fetch(`${API_BASE_URL}/api/dpp/products`);
      if (res.ok) {
        const data = await res.json();
        setProducts(data);
      }
    } catch (err) {
      console.error('Urunler getirilemedi:', err);
    }
  };

  useEffect(() => {
    fetchProducts();
  }, []);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      setFile(e.target.files[0]);
    }
  };

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!file) return;

    setLoading(true);
    setImportResult(null);

    const formData = new FormData();
    formData.append('file', file);

    try {
      const res = await fetch(`${API_BASE_URL}/api/dpp/import-csv`, {
        method: 'POST',
        body: formData,
      });

      const data: CsvImportResult = await res.json();
      setImportResult(data);
      fetchProducts();
    } catch (err) {
      console.error('CSV yukleme hatasi:', err);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ maxWidth: 1080, margin: '0 auto', padding: '32px 16px', fontFamily: 'system-ui, sans-serif', color: '#1e293b' }}>
      <header style={{ borderBottom: '1px solid #e2e8f0', paddingBottom: 20, marginBottom: 28 }}>
        <h1 style={{ fontSize: 26, margin: 0, fontWeight: 700, color: '#0f172a' }}>
          Mini Dijital Ürün Pasaportu (Mini DPP)
        </h1>
        <p style={{ margin: '6px 0 0', color: '#64748b', fontSize: 14 }}>
          ESG Next | TÜBİTAK 1507 Destekli Ar-Ge Projesi Teknik Değerlendirme Paneli
        </p>
      </header>

      {/* CSV Yukleme Kutusu */}
      <section style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: 12, padding: 24, marginBottom: 28, boxShadow: '0 2px 4px rgba(0,0,0,0.03)' }}>
        <h2 style={{ fontSize: 18, marginTop: 0, marginBottom: 16, display: 'flex', alignItems: 'center', gap: 8 }}>
          <Upload size={20} color="#2563eb" /> CSV İçe Aktarma ve Doğrulama
        </h2>
        <form onSubmit={handleUpload} style={{ display: 'flex', gap: 12, alignItems: 'center', flexWrap: 'wrap' }}>
          <input
            type="file"
            accept=".csv"
            onChange={handleFileChange}
            style={{ padding: '8px 12px', border: '1px solid #cbd5e1', borderRadius: 8, fontSize: 14 }}
          />
          <button
            type="submit"
            disabled={!file || loading}
            style={{
              background: !file || loading ? '#94a3b8' : '#2563eb',
              color: '#fff',
              border: 'none',
              borderRadius: 8,
              padding: '10px 20px',
              fontWeight: 600,
              cursor: !file || loading ? 'not-allowed' : 'pointer',
              display: 'flex',
              alignItems: 'center',
              gap: 6
            }}
          >
            {loading ? <RefreshCw className="spin" size={16} /> : <Upload size={16} />}
            {loading ? 'İşleniyor...' : 'CSV Doğrula ve Yükle'}
          </button>
        </form>

        {/* Dogrulama ve Import Raporu */}
        {importResult && (
          <div style={{ marginTop: 20, padding: 16, borderRadius: 8, background: importResult.failedCount > 0 ? '#fef2f2' : '#f0fdf4', border: `1px solid ${importResult.failedCount > 0 ? '#fecaca' : '#bbf7d0'}` }}>
            <div style={{ display: 'flex', gap: 16, fontSize: 14, fontWeight: 600, marginBottom: 8 }}>
              <span style={{ color: '#16a34a' }}>✓ {importResult.successCount} Satır Başarıyla Eklendi</span>
              {importResult.failedCount > 0 && (
                <span style={{ color: '#dc2626' }}>✕ {importResult.failedCount} Satır Reddedildi</span>
              )}
            </div>

            {importResult.errors.length > 0 && (
              <div style={{ marginTop: 12 }}>
                <div style={{ fontSize: 13, fontWeight: 600, color: '#991b1b', marginBottom: 6 }}>
                  Hata Raporu (Doğrulama İhlalleri):
                </div>
                <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13, background: '#fff' }}>
                  <thead>
                    <tr style={{ background: '#fee2e2', textAlign: 'left' }}>
                      <th style={{ padding: '6px 10px', border: '1px solid #fca5a5', width: 90 }}>Satır No</th>
                      <th style={{ padding: '6px 10px', border: '1px solid #fca5a5' }}>Hata Nedeni</th>
                    </tr>
                  </thead>
                  <tbody>
                    {importResult.errors.map((err, idx) => (
                      <tr key={idx}>
                        <td style={{ padding: '6px 10px', border: '1px solid #fecaca', fontWeight: 600 }}>Satır {err.rowNumber}</td>
                        <td style={{ padding: '6px 10px', border: '1px solid #fecaca', color: '#b91c1c' }}>{err.reason}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}
      </section>

      {/* Kayitli Urunler Tablosu */}
      <section style={{ background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: 12, padding: 24, boxShadow: '0 2px 4px rgba(0,0,0,0.03)' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 16 }}>
          <h2 style={{ fontSize: 18, margin: 0, display: 'flex', alignItems: 'center', gap: 8 }}>
            <FileText size={20} color="#0f172a" /> Kayıtlı Dijital Ürün Pasaportları ({products.length})
          </h2>
          <button
            onClick={fetchProducts}
            style={{ background: 'transparent', border: '1px solid #cbd5e1', padding: '6px 12px', borderRadius: 6, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 6, fontSize: 13 }}
          >
            <RefreshCw size={14} /> Yenile
          </button>
        </div>

        {products.length === 0 ? (
          <p style={{ color: '#64748b', fontSize: 14 }}>Henüz sisteme yüklenmiş geçerli bir ürün pasaportu bulunmuyor.</p>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 13, textAlign: 'left' }}>
              <thead>
                <tr style={{ background: '#f8fafc', borderBottom: '2px solid #e2e8f0' }}>
                  <th style={{ padding: '10px 12px' }}>GTIN (14 Hane)</th>
                  <th style={{ padding: '10px 12px' }}>Model</th>
                  <th style={{ padding: '10px 12px' }}>Parti / Lot</th>
                  <th style={{ padding: '10px 12px' }}>Üretim Yeri</th>
                  <th style={{ padding: '10px 12px' }}>Elyaf Dağılımı</th>
                  <th style={{ padding: '10px 12px' }}>İşlemler ve Çıktılar</th>
                </tr>
              </thead>
              <tbody>
                {products.map((p) => (
                  <tr key={p.gtin} style={{ borderBottom: '1px solid #f1f5f9' }}>
                    <td style={{ padding: '12px', fontFamily: 'monospace', fontWeight: 600 }}>{p.gtin}</td>
                    <td style={{ padding: '12px', fontWeight: 600, color: '#0f172a' }}>{p.modelName}</td>
                    <td style={{ padding: '12px' }}>{p.batchNo}</td>
                    <td style={{ padding: '12px' }}>{p.productionPlace}</td>
                    <td style={{ padding: '12px' }}>%{p.cottonPercentage} Pamuk / %{p.elastanePercentage} Elastan</td>
                    <td style={{ padding: '12px' }}>
                      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                        <a
                          href={`${API_BASE_URL}/dpp/${p.gtin}`}
                          target="_blank"
                          rel="noreferrer"
                          style={{ textDecoration: 'none', background: '#f1f5f9', color: '#0f172a', padding: '4px 8px', borderRadius: 4, display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, border: '1px solid #cbd5e1' }}
                        >
                          <Eye size={12} /> Tüketici HTML
                        </a>
                        <a
                          href={`${API_BASE_URL}/dpp/${p.gtin}?rol=yetkili`}
                          target="_blank"
                          rel="noreferrer"
                          style={{ textDecoration: 'none', background: '#ecfdf5', color: '#065f46', padding: '4px 8px', borderRadius: 4, display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, border: '1px solid #a7f3d0' }}
                        >
                          <ShieldCheck size={12} /> Yetkili Modu
                        </a>
                        <a
                          href={`${API_BASE_URL}/dpp/${p.gtin}.jsonld`}
                          target="_blank"
                          rel="noreferrer"
                          style={{ textDecoration: 'none', background: '#f8fafc', color: '#2563eb', padding: '4px 8px', borderRadius: 4, display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, border: '1px solid #bfdbfe' }}
                        >
                          JSON-LD
                        </a>
                        <button
                          onClick={() => setSelectedQrGtin(selectedQrGtin === p.gtin ? null : p.gtin)}
                          style={{ background: '#f8fafc', border: '1px solid #cbd5e1', padding: '4px 8px', borderRadius: 4, cursor: 'pointer', display: 'flex', alignItems: 'center', gap: 4, fontSize: 12 }}
                        >
                          <QrCode size={12} /> QR Kod
                        </button>
                      </div>

                      {selectedQrGtin === p.gtin && (
                        <div style={{ marginTop: 10, padding: 10, background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 8, textAlign: 'center', width: 140 }}>
                          <img
                            src={`${API_BASE_URL}/dpp/${p.gtin}/qr`}
                            alt="QR Kod"
                            style={{ width: 120, height: 120, display: 'block', margin: '0 auto' }}
                          />
                          <a
                            href={`${API_BASE_URL}/dpp/${p.gtin}/qr`}
                            download={`QR_${p.gtin}.png`}
                            style={{ fontSize: 11, color: '#2563eb', textDecoration: 'none', display: 'block', marginTop: 4 }}
                          >
                            PNG İndir
                          </a>
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}