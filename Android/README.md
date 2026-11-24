# Clover Event Detector for Android

The standalone Android detector uses the screen-sharing permission to scan landscape Clover Retribution chat about every three seconds. Recognition runs on the device using a 2-pass low-resolution-friendly OCR pipeline. The optional Discord webhook sends the event title and configured private server code (raw OCR broadcast text is only attached for unregistered `Other [Event] Broadcast` alerts). Stop capture in the app, via its notification, or via Android's screen-sharing controls.

1. Install the APK on Android 8.0 or newer.
2. Open **Clover Event Detector**, optionally add and test your Discord webhook, then tap **Mulai pemantauan**.
3. Approve Android's capture prompt. If Android offers app-only sharing, select Roblox. Open Clover in landscape and leave the chat visible.
4. If automatic region selection includes too much of the HUD, select Jungle (upper left) or First Kingdom (lower left) before starting the next session.

To rotate your server code, open **Server privat**, enter the new code and tap **Simpan kode**. The next alert uses the saved code even if capture is already running. Save an empty field to leave the code out of future alerts.

On older Android versions the OS can share the entire screen. Stop capture before switching to other apps with sensitive content. This detector has no gameplay or instance controls.

## Build and verification

Use JDK 17 or newer, Gradle 8.11.1, and the Android 35 SDK. Set ANDROID_HOME to that SDK and run:

    gradle :app:assembleDebug :app:lintDebug

The APK is at app/build/outputs/apk/debug/app-debug.apk. Run the :app:assembleDebugAndroidTest task to build the device-side OCR smoke test.
