# Clover Retribution Event Watcher

Event Watcher membaca chat Clover Retribution dengan OCR dan dapat mengirim notifikasi event baru ke Discord.

- [Android](Android/README.md) mendukung Android 8.0 ke atas. Buat APK dari source dengan petunjuk build, lalu isi webhook, kode Private Server, dan ID pengguna Discord sesuai kebutuhan. Tekan **Mulai pemantauan** dan izinkan tangkap layar.
- [Windows](Legacy-Windows/README.md) berisi watcher WPF x64. Buat executable dengan menjalankan `powershell -ExecutionPolicy Bypass -File .\build.ps1` dari folder `Legacy-Windows`. Pengaturan Discord tersedia di tab **Event Watcher & Anti-AFK**.

Pengaturan disimpan secara lokal. Android menggunakan preferensi aplikasi dan Windows menggunakan file yang dicakup oleh `.gitignore`.
