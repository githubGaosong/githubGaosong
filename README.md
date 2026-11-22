# Translate online

A lightweight Windows x64 desktop translator with a translucent, customizable WPF interface.

## Features

- Online text translation using the MyMemory translation API.
- Automatic source-language detection and a broad selection of major world languages.
- Translated text remains editable and can be cleared independently.
- Translucent glass appearance with selectable solid-color skins and user-provided image backgrounds.
- Language-aware interface labels.
- Always-on-top mode, draggable/resizable window, and global shortcuts: **F5** to translate and **F4** to hide/show.
- Equal-width source and target language selectors, full input and translation panels, and the finalized left/center/right footer-button layout.

## Build

Requires the **.NET 8 Desktop Runtime (Windows x64)**.

```powershell
dotnet publish "Translate.csproj" -c Release -r win-x64 --self-contained false
```

The compact single-file application is generated at:

`bin/Release/net8.0-windows/win-x64/publish/Translate online.exe`

The framework-dependent publish keeps the executable small by using the installed .NET 8 Desktop Runtime rather than embedding the runtime in the application.

## Privacy and service notes

- Translation requires an internet connection and uses the public MyMemory API.
- Text submitted for translation is sent to that third-party service and is subject to its availability and rate limits.
- The application does not provide OCR or automatically read text from other windows.
- Exclusive full-screen applications may prevent an always-on-top window from appearing; borderless-window mode is recommended.

## Project structure

- `Program.cs` — application entry point.
- `TranslateWindow.cs` — WPF window, UI layout, styling, and interactions.
- `LanguageCatalog.cs` — supported languages and localized UI strings.
- `TranslationService.cs` — online translation requests and text chunking.
- `Translate.csproj` — build and publishing configuration.