# LumeFetch Android 1.1.0-beta.1

İlk herkese açık Android beta sürümü. **Android 8+ / ARM64** cihazlar içindir.
Windows v1.0.0 indirmeleri değişmedi.

## İndir

`LumeFetch-Android-1.1.0-beta.1-arm64.apk` — yaklaşık 60 MiB.
FFmpeg, FFprobe, Python, yt-dlp ve QuickJS paket içinde; ilk açılışta ek araç
indirmez. İnternet yalnızca içerik analizi/indirme gibi çevrimiçi işler için gerekir.

- MP3/MP4, kalite seçimi, YouTube oynatma listeleri, indirme kuyruğu ve TR/EN.
- S25 Ultra / Android 16’da MP3/MP4’ü engelleyen kütüphane çakışması düzeltildi.
  Build 7’nin iki formatta çalıştığı proje sahibi tarafından doğrulandı.
- Geçici geliştirici/tanılama kartı Ayarlar’dan kaldırıldı.
- Hata bildirimi görünürken kalite listesinin dar ekrana uyumu düzeltildi.
- Kalıcı yayın imzası, dosya özetleri, açık kaynak lisansları ve eşleşen kaynaklar.
- Spotify kapalı. iOS sürümü yok; iOS çalışması beklemede.

## Kurulum ve test APK’sından geçiş

APK’yı yalnızca bu resmi GitHub sayfasından alın; `SHA256SUMS.txt` ile kontrol edin.
Android isterse yalnızca APK’yı açtığınız uygulama için kurulum izni verin; güvenlik
korumalarını topluca kapatmayın. Ayarlar’dan yazılabilir bir klasör seçip kaydedin.

**Eski özel test APK’sı farklı imzalıdır:** yayın APK’sı onun üzerine kurulamaz.
Önemli indirmeleri önce tamamlayıp dışarı aktarın. Test uygulamasını kaldırmayı
seçerseniz özel ayarlar, geçmiş ve tamamlanmamış dosyalar silinir. Otomatik kaldırma
yapılmıyor. Sonraki herkese açık güncellemeler aynı kalıcı anahtarla imzalanacaktır.

## Neden beta?

110 birim testi, telefon boyutlarında arayüz testleri, paket/imza/bütünlük ve
177 ELF hizalama kontrolü geçti. Son Release arayüzü emülatörde gerçek MP3
aktarımı ve dışa aktarma ile doğrulandı. Telefonun MP3/MP4 onayı build 7 içindir;
son imzalı beta paketi henüz telefonda yeniden denenmedi. Tüm platformlar,
Android sürümleri, uzun arka plan çalışması, depolama hataları ve gerçek 16-KB
cihaz matrisi henüz tamamlanmadı. Bu nedenle Android için “stabil” denmiyor.

Android’in arka plan sınırlamaları geçerlidir. Uygulama kapatıldıktan sonra
tamamlanmamış işler duraklatılmış olarak geri gelir. Sosyal platformların
erişim kuralları değişebilir; her bağlantının çalışması garanti edilmez.

## Açık kaynak / doğrulama

LumeFetch’in kodu MIT; paket içindeki araçların lisansları ayrıdır. Android
FFmpeg LGPLv3 yapılandırması kullanır. Aynı yayındaki `corresponding-sources.zip`
tam eşleşen yerel araç/Python kaynaklarını, yamaları, derleme tariflerini ve
uygulama kaynağını içerir. `notices.zip` bildirimleri APK içinde de bulunur.

APK SHA-256: `571661a4f21b09683ee3bf2bd0c8f84ee69385bf9a535c306a56f23813115a79`

Sertifika SHA-256: `D9A78375002A9948147BF17E03F9E7E60840476E56DAA4BBB0C4B782AC376B90`

Yalnızca indirmeye yetkili olduğunuz içeriklerde kullanın. DRM, ödeme duvarı veya
erişim kontrolü aşma amacı taşımaz. Özel anahtar ve şifreler dağıtıma dahil değildir.

---

First public **Android ARM64 beta**, minimum Android 8/API 26, version code 10.
All runtime tools are bundled. Removes the temporary diagnostics UI and retains
the S25 library-isolation fix. MP3/MP4 passed maintainer testing on build 7;
the final signed beta is not yet physically retested. Wider device/provider and
background/storage coverage remains beta work. Spotify is disabled; iOS is on hold.

The permanent release certificate differs from private debug builds. Export your
important downloads before choosing to remove a test build: removal loses private
settings/history/unfinished files. No automatic uninstall occurs. See the
matching source and notice archives and SHA256SUMS; Windows v1.0.0 is unchanged.
