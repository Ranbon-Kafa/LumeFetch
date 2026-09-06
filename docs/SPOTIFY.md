# Spotify → YouTube — v1'de kapalı / disabled in v1

**v1 kullanıcıları:** Bu özellik hizmet politikalarının değerlendirilmesi için
kapalıdır. Spotify girişi ve bağlantı çözümleme kullanılamaz. Açmak için gizli
bir ayar yoktur; aşağıdaki bilgiler deneysel kaynak koduna aittir.

The maintainer approved disabling Spotify in v1 on 2026-09-06. The desktop
composition root registers no Spotify resolver or OAuth session. The experimental
code and tests remain for future policy assessment, not as an enabled v1 feature.
Spotify's [Developer Policy](https://developer.spotify.com/policy), particularly
III.5 and III.9, restricts cross-service integration and data transfer. A successful
login or matching test does not imply distribution approval. The following is
historical developer documentation, **not v1 end-user setup guidance**.

## Türkçe hızlı kurulum

1. [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) üzerinden
   kendi uygulamanı oluştur. Geliştirici hesabının erişim koşullarını kontrol et.
2. Uygulamanın Redirect URI alanına **tam olarak** şunu kaydet:
   `http://127.0.0.1:43821/callback`
3. LumeFetch → Ayarlar → Spotify bölümüne uygulamanın **Client ID** değerini gir.
   **Client Secret gerekmez; uygulamaya veya sohbete yazma.**
4. **Spotify'a bağlan** düğmesine bas, açılan tarayıcıda izin ver ve LumeFetch'e dön.
   Ayarları kaydet. Client ID gizli değildir; erişim/yenileme tokenları yalnızca
   bellekte tutulur. Uygulamayı kapattıktan sonra yeniden bağlanman gerekir.
5. Ana ekrana Spotify şarkı, albüm veya erişebildiğin oynatma listesi bağlantısını
   yapıştır. **YouTube eşleşmelerini bul** ile seçili parçaları ara.
6. Eşleşmeleri gözden geçir. Gerekiyorsa alternatif videoyu seç, indirmeye yetkili
   olduğun parçaları işaretle ve **Seçilenleri kuyruğa ekle** düğmesini kullan.

MP3 istiyorsan toplu kalite listesinden **MP3 ses** seç. Dönüştürme seçtiğin
YouTube kaynağı üzerinde FFmpeg ile yapılır; Spotify ses akışı kullanılmaz.

90 ve üzeri sonuçlar önceden seçilir. 70–89 doğrulama gerektirir; 70 altı
sonuçlarda da doğru kaynağı kendin seçmelisin. Bulunamayan parça indirilemez.
Puan bir olasılık veya indirme izni değildir.

## How it works

Spotify URL → official Web API metadata → explicit YouTube search → scored
candidates → user-reviewed selection → normal provider analysis and download.
Single tracks use the same review screen as albums/playlists.

- Title similarity: up to 35 points.
- Artist token coverage: up to 35 points.
- Absolute duration difference strictly below 3 seconds: 20 points.
- Verified channel or Topic suffix heuristic: 10 points.
- Live/remix/cover/karaoke/instrumental/sped/slowed version conflicts cap the score
  at 69. These are conservative heuristics, not proof of ownership or accuracy.
- Up to five YouTube candidates per track; preview capped at 200 catalog items.
- ISRC is retained when Spotify returns it; it is not currently used as a
  YouTube identity check. Missing duration cannot receive duration points.

The downloader never reads a Spotify audio stream. Spotify metadata is not used
to tag the output file or embed Spotify cover art; the selected YouTube source
provides download metadata. Spotify catalog links remain visible for attribution.
Selecting search shares only the selected artist/title query with YouTube.

## Access, security and release gates

- A public desktop client uses [Authorization Code with PKCE](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow).
  No shared app secret is bundled. Authentication state is checked, callback
  listening is restricted to 127.0.0.1, and token requests use HTTPS.
- Tokens stay in process memory, never in settings or logs. Disconnect clears
  them. The saved Client ID is public application identification, not a secret.
- Current [playlist item access](https://developer.spotify.com/documentation/web-api/reference/get-playlists-items)
  is restricted to owned/collaborative playlists. The resolver uses
  `/v1/playlists/{id}/items`, paginates safely, and marks local files/podcasts/
  unavailable entries as unavailable. Public visibility alone is insufficient.
- [Development-mode restrictions](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
  include Premium/allowlisting and user limits. A shared production Client ID
  cannot be assumed to work for arbitrary users.
- 401, 403 and rate limits are shown as errors, without an undocumented scraping
  fallback. API pagination cannot redirect bearer tokens to another host;
  the application's Spotify HTTP client disables automatic redirects.
- Before public distribution, review the [Spotify Developer Policy](https://developer.spotify.com/policy),
  especially cross-service integrations, attribution/official branding, data
  handling and quota eligibility. This implementation is **not** a statement of
  Spotify endorsement, approval or permission to download any recording.
- Loopback OAuth/refresh is tested with a mock token server. A real Spotify
  developer account, live consent, catalog access and live search matching still
  require end-to-end acceptance testing by the developer.

## Troubleshooting

- No developer access / 403: check account eligibility and allowlisted users;
  use an owned/collaborative playlist. Do not try to bypass restrictions.
- Redirect mismatch: register the exact URI above, including port and path.
- Port 43821 in use: close the conflicting local login attempt and reconnect.
- Login timeout: return to Settings and start a new connection.
- No/poor matches: choose another candidate, correct the source manually using
  the normal URL field, or skip the item. Do not treat a score as certainty.
