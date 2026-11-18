# NEET BEE mobile app (Android and iOS)

A thin .NET MAUI app (`NeetBee.Mobile`) that opens the NEET BEE website in a full-screen WebView.
The website already handles phone layouts, so the app only hosts it.

- **Android back button** goes back in the website's history; on the first page it closes the app.
- **iOS**: swipe from the screen edge to go back/forward in the website's history.
- **No internet**: shows a "No internet connection" screen with a **Retry** button, and reloads by itself when the connection comes back.
- `tel:`, `mailto:`, `whatsapp:` and similar links open in the phone's own apps.
- App icon: the NEET BEE bee on white. Splash screen: the full NEET BEE logo on white. Both come from `WebApplication/wwwroot/img`.

Targets `net11.0-android` and `net11.0-ios` (the .NET 11 SDK cannot build the out-of-support .NET 8 MAUI targets).

## Changing the website URL

Edit [`appsettings.json`](appsettings.json) in this folder:

```json
{
  "App": {
    "StartUrl": "https://neetbee-dev-95747231-cyg7b0dra8f3d5eb.southindia-01.azurewebsites.net/"
  }
}
```

The file is embedded in the app when it is built, so **rebuild and reinstall** after changing it.
The app refuses to start if `StartUrl` is missing or not an `http`/`https` address.

## Prerequisites

- .NET 11 SDK
- MAUI workloads for Android and iOS. Check with `dotnet workload list`; if `maui-android` / `maui-ios` are missing, install them:

  ```bash
  dotnet workload install maui-android maui-ios
  ```

  (Visual Studio users: install the ".NET Multi-platform App UI development" workload in the Visual Studio Installer instead.)
- Android: the Android SDK and an emulator or a phone with USB debugging (Visual Studio installs these with the MAUI workload).
- iOS: a Mac with Xcode. On Windows, Visual Studio can build iOS only while paired to a Mac.

## Run on Android

From the repository root:

```bash
dotnet build MobileApp/NeetBee.Mobile.csproj -f net11.0-android -t:Run
```

This builds, installs and starts the app on the running emulator or connected phone.
In Visual Studio, set **NeetBee.Mobile** as the startup project, pick an Android emulator/device from the run target list and press F5.

To make an APK/AAB for testing or the Play Store:

```bash
dotnet publish MobileApp/NeetBee.Mobile.csproj -f net11.0-android -c Release
```

## Run on iOS

On a Mac with Xcode installed:

```bash
dotnet build MobileApp/NeetBee.Mobile.csproj -f net11.0-ios -t:Run
```

This starts the iOS Simulator. Running on a real iPhone or publishing to the App Store needs an Apple Developer account and signing set up (`CodesignKey` / `CodesignProvision`).

## CI builds (GitHub Actions)

[`.github/workflows/mobile-build.yml`](../.github/workflows/mobile-build.yml) builds the app on GitHub:

- **When:** on every push to `dev` that changes `MobileApp/**`, or manually from **Actions → Build NEET BEE Mobile App (Android + iOS) → Run workflow** (choose the branch and Release/Debug).
- **Android** (Windows runner): builds an `.apk` (install on a phone for testing) and an `.aab` (Play Store format), uploaded as the `neetbee-android-…` artifact. They are signed with the default debug key, so they are fine for testing but not for the Play Store.
- **iOS** (macOS runner): builds for the iOS Simulator, uploaded as the `neetbee-ios-simulator-…` artifact. This needs no Apple certificate, but it does not run on a real iPhone.
- **Downloading a build:** open the workflow run, click **Summary** (house icon, top of the left sidebar) and download it from **Artifacts** at the bottom of the page. Artifacts are kept for 7 days. For Android, install `com.neetbee.mobile-Signed.apk`.
- Nothing is published to the stores.

Store-ready builds need signing secrets added to the workflow later: an Android release keystore (`AndroidKeyStore`, `AndroidSigningKeyStore`, `AndroidSigningKeyAlias`, `AndroidSigningKeyPass`, `AndroidSigningStorePass`) and, for iOS, an Apple distribution certificate and provisioning profile (`CodesignKey`, `CodesignProvision`).

## Notes

- The app ID is `com.neetbee.mobile` (`ApplicationId` in `NeetBee.Mobile.csproj`). Choose the final ID before the first store upload; it cannot be changed afterwards.
- **Google sign-in**: Google blocks its sign-in page inside embedded WebViews ("disallowed_useragent"), so "Login with Gmail" will not work in the app as is. Email/password login is unaffected. Fixing this needs native sign-in (for example `WebAuthenticator`) and is not part of this shell.
- The website workflows in `.github/workflows/` build `WebApplication/WebApplication.csproj` directly, so this project does not affect them.
