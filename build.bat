@echo off
REM ============================================================
REM  AIInfluence Prism - build
REM  Reference assemblies come from the game via fetch-refs.ps1.
REM ============================================================
setlocal
cd /d "%~dp0"

REM Приоритет тулчейна: переносимый SDK в _toolchain, потом C:\dotnet8, потом PATH.
set "TOOLCHAIN=%~dp0..\_toolchain\dotnet-sdk"
if exist "%TOOLCHAIN%\dotnet.exe" (
    set "DOTNET_ROOT=%TOOLCHAIN%"
    set "PATH=%TOOLCHAIN%;%PATH%"
) else if exist "C:\dotnet8\sdk" (
    set "DOTNET_ROOT=C:\dotnet8"
    set "PATH=C:\dotnet8;%PATH%"
)

if not exist "libs\TaleWorlds\TaleWorlds.Core.dll" (
    echo [Prism] Fetching reference assemblies...
    powershell -NoProfile -ExecutionPolicy Bypass -File "fetch-refs.ps1" -GameFolder "%BannerlordFolder%"
    if errorlevel 1 (
        echo [Prism] ERROR: fetch-refs failed. Pass -GameFolder or set BannerlordFolder.
        exit /b 1
    )
)

where dotnet >NUL 2>&1
if errorlevel 1 (
    echo [Prism] ERROR: .NET SDK not found in PATH ^(expected C:\dotnet8 or system dotnet^).
    exit /b 1
)

echo [Prism] Building...
dotnet build "src\AIInfluencePrism.csproj" -c Release --nologo
if errorlevel 1 (
    echo [Prism] BUILD FAILED
    exit /b 1
)

if "%1"=="-SkipTests" goto :done
if not exist "tests\AIInfluencePrism.Tests.csproj" goto :done
echo [Prism] Running tests...
dotnet test "tests\AIInfluencePrism.Tests.csproj" -c Release --nologo -v q
if errorlevel 1 (
    echo [Prism] TESTS FAILED
    exit /b 1
)

:done
echo.
echo [Prism] Build OK: bin\Win64_Shipping_Client\AIInfluencePrism.dll
endlocal
