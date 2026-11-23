# RoMulti — Roblox Multi-Instance, AFK Saver & Clover Retribution Event Watcher

Aplikasi desktop Windows native (WPF x64) untuk mengelola instance Roblox dan mengirim alert *Clover Retribution* ke Discord dengan OCR bawaan Windows.

## Fitur Lengkap

### Tab 1: Instances & Handles (Tahap 1)
- **Multi-Instance Unlock (`Auto-Unlock` + `Unlock All`):** Menutup `ROBLOX_singletonEvent` & `ROBLOX_singletonMutex` otomatis.
- **Roblox Handles Inspector:** Menampilkan khusus handle Event/Mutant milik `RobloxPlayerBeta.exe`.
- **Mode `AFK SAVER` per-instance (Akun `MAIN` tidak pernah disentuh):**
  - **Auto RAM Trim (`EmptyWorkingSet`)** tiap 45 detik + tombol manual **Trim AFK RAM**.
  - **EcoQoS (Efficiency Mode) + CPU Priority `BelowNormal`**.
  - **Compact Window `520×320` (unminimized & tanpa mencuri fokus)** agar hemat GPU sekaligus tetap ter-render untuk OCR.
  - **Auto-Mute Per-Proses** via Windows Core Audio (otomatis unmute jika dikembalikan ke `MAIN`).
  - **KPI Free System RAM** real-time + optimasi RAM `RoMulti.exe` sendiri.
- **Window Controls:** `Focus`, `Rename`, `Tile Grid`, `Side-by-Side`, `Launch Roblox`, `Kill / Kill All`, `System Tray`.

### Tab 2: Event Watcher & Anti-AFK (Tahap 2)
- **Windows WinRT OCR Bawaan (2-Pass Color Isolation):**
  - Scan area kotak chat kiri-atas setiap instance `AFK SAVER` tiap 5 detik (tanpa install Python/Tesseract/API).
  - **Pass 1 (High-Saturation Isolation):** memisahkan teks berwarna **ungu** (*Devil Rift / Khamsin*), **emas** (*Spirit Rift*), **biru**, hijau, merah bahkan saat latar belakang langit/gurun terang.
  - **Pass 2 (Bright Text):** menangkap teks sistem putih di kotak chat gelap.
  - **Test Capture Now + Live Preview:** langsung menampilkan gambar crop chat & teks OCR terbaca (termasuk peringatan otomatis jika frame hitam karena tertutup penuh di 1 monitor).
- **18 Rules Clover Retribution (Tersimpan di `romulti-config.json`):**
  - **PING (`@everyone` / `<@UserID>`):** **Devil Rift** (`Something feels off...`) & **Spirit Rift** (`strange energy feel toward kingdom`).
  - **NORMAL:** Khamsin, Void Wisp, Spirit Wisp, Strange Scientist, Devil Rumors, Guarded Rift, Devil Assault, Corrupted Night, Green Star, The Divine General, Prime Apex, serta **Catch-All `[Event]`** agar event baru tidak pernah kelewat.
  - **IGNORE (default):** Mana Wisp, Terra, Grasslands Warlord, Wolf Hunter Craftsman *(klik baris di UI untuk siklus `PING` ↔ `NORMAL` ↔ `IGNORE`)*.
- **Discord Webhook Rich Embed:**
  - Mengirim nama event, teks chat hasil OCR, label Scout & PID, **PlaceId + JobId + Direct Join URI** otomatis dari log Roblox, lampiran **crop screenshot chat**, dan **anti-spam deduplikasi 5 menit** per `(PID, Event)`.
  - **Alert Clover Library Teleport:** otomatis mengirim peringatan Discord jika `PlaceId` akun AFK berubah mendadak di log.
- **Smart Anti-AFK Keep-Alive (Clover Library ~15m + Roblox 20m Kick):**
  - Tiap **11 menit** (bisa diatur 8–14 menit), menunggu jeda singkat saat Anda tidak sedang menahan tombol combat/mouse, lalu melakukan quick-pulse ~0,7 detik per akun AFK (**`W` 180ms → `S` 180ms + `Space`** pakai hardware scan-code `SendInput`) dan langsung mengembalikan fokus & kursor ke window Main Anda.

## Cara Menggunakan (Setup 1 Main + 2 AFK)

> Panduan lengkap pembuatan & setup Discord Webhook lihat di [DISCORD_WEBHOOK.md](DISCORD_WEBHOOK.md).

1. Jalankan [RoMulti.exe](file:///D:/Romulti/RoMulti.exe).
2. Di tab **Instances & Handles**: biarkan akun pertama sebagai **MAIN**, lalu klik **Set AFK Saver** pada 2 akun scout (atau aktifkan **Auto-AFK new** + **Auto-Unlock: ON**).
3. Di tab **Event Watcher & Anti-AFK**:
   - Paste **Discord Webhook URL** Anda → klik **Save Config** → klik **Test Webhook**.
   - Klik **Test Capture Now** pada akun scout untuk memastikan kotak chat terbaca jelas (tidak hitam). *(Di 1 monitor: jika tertutup penuh oleh Main membuat frame hitam, posisikan sebagian kecil/pojok window AFK agar tetap terlihat oleh Windows DWM.)*
   - Aktifkan **Event Watcher: ON** dan **Anti-AFK: ON**.

## Build Ulang dari Source

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```
