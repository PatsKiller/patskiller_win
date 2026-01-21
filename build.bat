@echo off
echo ==========================================
echo PatsKiller Pro v2.0 Build Script
echo ==========================================
echo.

REM Check if dotnet is available
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET 8 SDK.
    pause
    exit /b 1
)

REM Show .NET version
echo .NET Version:
dotnet --version
echo.

REM Build options
echo Select build type:
echo 1. Debug build
echo 2. Release build (self-contained)
echo 3. Release build with NativeAOT (production)
echo.

set /p choice="Enter choice (1-3): "

if "%choice%"=="1" (
    echo.
    echo Building Debug...
    dotnet build PatsKillerPro.sln -c Debug
    if %ERRORLEVEL% equ 0 (
        echo.
        echo Build successful!
        echo Output: PatsKillerPro\bin\Debug\net8.0-windows\
    )
)

if "%choice%"=="2" (
    echo.
    echo Building Release (self-contained)...
    dotnet publish PatsKillerPro\PatsKillerPro.csproj -c Release -r win-x64 --self-contained
    if %ERRORLEVEL% equ 0 (
        echo.
        echo Build successful!
        echo Output: PatsKillerPro\bin\Release\net8.0-windows\win-x64\publish\
    )
)

if "%choice%"=="3" (
    echo.
    echo Building Release with NativeAOT...
    echo NOTE: This requires NativeAOT to be enabled in .csproj
    dotnet publish PatsKillerPro\PatsKillerPro.csproj -c Release -r win-x64 --self-contained -p:PublishAot=true
    if %ERRORLEVEL% equ 0 (
        echo.
        echo Build successful!
        echo Output: PatsKillerPro\bin\Release\net8.0-windows\win-x64\publish\
    )
)

echo.
pause
