@echo off
dotnet publish GameTranslator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
echo.
echo EXE: bin\Release\net8.0-windows\win-x64\publish\GameTranslator.exe
pause
