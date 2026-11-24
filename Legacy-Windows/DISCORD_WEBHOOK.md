# Panduan Setup Discord Webhook — RoMulti (Clover Retribution)

Panduan lengkap cara membuat **Discord Webhook**, menghubungkannya ke **RoMulti**, mengatur ping prioritas (*Devil Rift* & *Spirit Rift*), serta cara menguji agar notifikasi event langsung masuk ke Discord dalam **1–4 detik**.

---

## 1. Cara Membuat Discord Webhook

1. Buka **Discord** (Desktop atau Web).
2. Pilih server milik Anda *(disarankan buat server pribadi khusus notifikasi farming agar tidak mengganggu orang lain)*, lalu pilih atau buat 1 text channel (mis. `#clover-events`).
3. Klik ikon **Gerigi (Edit Channel)** di sebelah nama channel tersebut.
4. Di menu kiri, pilih **Integrations** → klik **Webhooks** → klik **New Webhook** (atau **Create Webhook**).
5. Beri nama bebas (mis. `RoMulti Scout`) dan pastikan channel tujuannya sudah benar.
6. Klik **Copy Webhook URL** — formatnya seperti:
   ```text
   https://discord.com/api/webhooks/123456789012345678/AbCdEfGhIjKlMnOpQrStUvWxYz...
   ```

> [!CAUTION]
> **Jangan bagikan Webhook URL Anda ke siapa pun.** Siapa saja yang memiliki URL tersebut bisa mengirim pesan spam ke channel Discord Anda. Jika tidak sengaja bocor, hapus webhook lama di Discord lalu buat baru.

---

## 2. Cara Menghubungkan ke RoMulti

1. Jalankan [RoMulti.exe](file:///D:/Romulti/RoMulti.exe).
2. Di tab **Instances & Handles**:
   - Biarkan akun utama Anda berstatus **`MAIN`**.
   - Klik akun scout Anda lalu klik **`Set AFK Saver`** *(Event Watcher hanya memindai instance bertanda `AFK SAVER`)*.
3. Pindah ke tab **Event Watcher & Anti-AFK**:
   - Paste **Webhook URL** ke kolom URL paling atas.
   - Atur kolom **Mention / Ping** di sebelah kanan URL:
     - `@everyone` → ping semua anggota di channel (cocok untuk server pribadi).
     - `<@USER_ID>` → ping hanya akun Discord Anda (contoh: `<@284910293847102938>`).
     - `<@&ROLE_ID>` → ping role tertentu.
   - Pastikan opsi yang direkomendasikan aktif:
     - `[x] Attach screenshot` — melampirkan potongan gambar chat (`chat.png`) di embed Discord.
     - `[x] Alert Clover Library` — mengirim peringatan jika akun AFK ter-teleport pindah map (*PlaceId* berubah).
     - `[x] BG chat hover` — mengirim hover latar belakang ke area chat tanpa mencuri fokus.
     - `[x] Wake chat (/+Enter)` — membangunkan kotak chat gelap setelah pulse Anti-AFK.
4. Klik **Save Config** *(tersimpan otomatis di `D:\Romulti\romulti-config.json`)*.
5. Klik **Test Webhook** → cek channel Discord Anda; pesan hijau **"RoMulti Event Watcher — Test Connected"** harus langsung masuk.

### Cara Mengambil User ID Discord (Opsional untuk `<@USER_ID>`)
1. Di Discord: **User Settings** (ikon gerigi kiri-bawah) → **Advanced** → aktifkan **Developer Mode**.
2. Klik kanan profil Anda sendiri → klik **Copy User ID**.
3. Masukkan ke kolom Ping RoMulti dengan format `<@PASTE_ID_DI_SINI>`.

---

## 3. Pengaturan Prioritas Event Clover Retribution

Di panel kanan tab **Event Watcher & Anti-AFK**, klik baris event mana saja untuk mengganti level notifikasinya (**`PING`** → **`NORMAL`** → **`IGNORE`**). Perubahan langsung tersimpan.

| Level | Perilaku di Discord | Daftar Default di RoMulti |
|---|---|---|
| **`PING`** | Mengirim **mention** (`@everyone` / `<@USER_ID>`) + embed + screenshot chat | **Devil Rift** (`Something feels off...`, ungu)<br>**Spirit Rift** (`A strange energy is felt throughout the kingdom`, kuning/emas)<br>**Wind Wisp** (hijau), **Blood Wisp** (merah), **Light Wisp** (kuning/emas), **Spirit Wisp** |
| **`NORMAL`** | Mengirim embed + screenshot chat **tanpa mention/ping** | **Khamsin: The Sand Sage**, **Void Wisp**, **Leak Event**, **Strange Scientist**, **Guarded Rift**, **Corrupted Night**, **A Green Star**, **The Divine General**, **Prime Apex**, **1–7 Stars**, **Other `[Event]` Broadcast** *(Catch-All)* |
| **`IGNORE`** | Tidak dikirim ke Discord | **Mana Wisp**, **Terra: The Bandit Lord**, **Grasslands Warlord**, **Wolf Hunter Craftsman**, **Devil Rumors**, **Devil Assault (Giant Demon)**, **Vampire Castle / Lord Lucius**, **Raid: Kaldris (Embersworn)**, **Primal Werewolf**, **Jungle Bosses (Crocodile / Mammoth)** |

> [!TIP]
> Aturan **Other `[Event]` Broadcast (Catch-All)** menangkap semua baris berawalan `[Event]` yang kalimatnya belum terdaftar, sehingga event baru dari update game tidak akan pernah terlewat.

---

## 4. Contoh Data yang Dikirim ke Discord

Setiap kali event terdeteksi pada salah satu akun `AFK SAVER`, RoMulti mengirim **Rich Embed** berisi:

- **Judul & Warna Embed:** sesuai jenis event (mis. Ungu untuk *Devil Rift*, Emas untuk *Spirit Rift*).
- **Chat Broadcast (OCR):** hanya ditampilkan di deskripsi embed untuk event yang belum terdaftar (**Other `[Event]` Broadcast**); event terdaftar tampil ringkas tanpa teks OCR mentah.
- **Scout Account:** label akun & PID tempat event muncul (mis. `Scout 1 (PID 32252)`).
- **Server (`PlaceId` / `JobId`):** dibaca otomatis dari log aktif Roblox.
- **Quick Join URI:** link `roblox://experiences/start?placeId=...&gameInstanceId=...` untuk langsung melompat ke server tersebut.
- **Lampiran Gambar (`chat.png`):** potongan tangkapan layar kotak chat untuk verifikasi instan.

### Anti-Spam Dedupe & Auto-Retry
- **Timeline Memory (30 menit) & Cross-PID Same-`JobId` Dedupe (90 detik):** RoMulti otomatis membedakan pesan chat lama vs spawn event baru di bawah chat, serta menahan duplikat lintas-instance ketika dua akun berada di server (`JobId`) yang sama — sementara dua akun di server berbeda (*1st Kingdom* + *Jungle*) atau spawn kedua di akun yang sama tetap terkirim normal.
- **Webhook Auto-Retry:** Jika terjadi error sementara (`HTTP 429`, `5xx`, atau timeout jaringan), RoMulti otomatis mencoba ulang hingga **3×** (menghormati header `Retry-After`) tanpa mengulang error permanen (`400`/`401`/`403`/`404`).

---

## 5. Cara Uji End-to-End Sebelum Ditinggal AFK

1. **Cek Preview Capture (Wajib di 1 Monitor):**
   - Posisikan window **Main** di depan window **AFK Saver** (`520×320`, **jangan di-minimize**).
   - Klik **Test Capture** (atau **Wake & Capture**) di RoMulti.
   - Pastikan gambar kotak chat terlihat jelas dan teks OCR muncul di kotak kanan.
   - Jika tampil **`[BLACK FRAME]`**, berarti driver D3D11 berhenti me-render saat window tertutup 100% — sisakan sedikit bagian/pojok window AFK terlihat di pinggir layar.
2. **Tes Live 10 Menit (Tanpa Menunggu Devil/Spirit Rift):**
   - Klik baris **Mana Wisp** di tabel kanan agar sementara menjadi **`NORMAL`**.
   - Aktifkan **`Event Watcher: ON`** (dan **`Anti-AFK: ON`**).
   - Tunggu 1 siklus event (~10 menit) sampai `[Event] Mana fluttering...` muncul dan masuk ke Discord.
   - Setelah terbukti masuk otomatis, klik **Mana Wisp** lagi kembali ke **`IGNORE`**.

---

## 6. Konfigurasi Manual (`romulti-config.json`)

Semua pengaturan disimpan di `D:\Romulti\romulti-config.json` (contoh):

```json
{
  "WebhookUrl": "https://discord.com/api/webhooks/...",
  "PingText": "@everyone",
  "ScanIntervalSeconds": 3,
  "AttachScreenshot": true,
  "AlertOnMapChange": true,
  "BackgroundChatHover": true,
  "WakeChatOnAntiAfk": true,
  "AntiAfkEnabled": true,
  "AntiAfkIntervalMinutes": 11,
  "EventLevels": {
    "devil_rift": "Ping",
    "spirit_rift": "Ping",
    "mana_wisp": "Ignore"
  }
}
```

*(Jika Anda mengubah `ScanIntervalSeconds` secara manual di file JSON, restart `RoMulti.exe` agar timer scan baru aktif.)*

---

## 7. Troubleshooting Cepat

| Masalah | Penyebab & Solusi |
|---|---|
| `Discord HTTP 401` / `404` | Webhook URL salah, terpotong, atau webhook sudah dihapus di Discord → copy ulang URL baru & klik **Save Config**. |
| `Discord HTTP 429` | Rate limit Discord karena terlalu banyak test klik berturut-turut → tunggu beberapa detik. |
| Event tidak terkirim padahal muncul di chat | 1) Pastikan akun ditandai **`AFK SAVER`** (bukan `MAIN`).<br>2) Pastikan toggle **`Event Watcher: ON`**.<br>3) Cek status event di tabel kanan tidak di-set `IGNORE`.<br>4) Klik **Test Capture** untuk memastikan tidak `[BLACK FRAME]` dan ikon chat kiri-atas Roblox aktif. |
| Notif tidak muncul di HP/PC Discord | Di Discord, klik kanan channel `#clover-events` → **Notification Settings** → pilih **All Messages** atau **Only @mentions** (dan pastikan channel/server tidak di-mute). |
| Akun AFK malah masuk *Clover Library* | Pastikan **`Anti-AFK: ON`** (interval **10–11 menit**) dan kotak ketik chat `/` **tidak dibiarkan terbuka**. |
