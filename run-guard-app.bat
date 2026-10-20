
@echo off
cd /d "%~dp0"

echo ==========================================
echo       BCP PARKING GUARD APPLICATION
echo ==========================================
echo.

echo Checking Guard App project...

if not exist "guard-app\BcpParking.Api.csproj" (
    echo ERROR: BcpParking.Api.csproj not found!
    echo Check that the guard-app folder exists.
    pause
    exit /b 1
)

echo.
echo Starting BCP Guard App...
echo Make sure start-server.bat is already running.
echo.

dotnet run --project "guard-app\BcpParking.Api.csproj"

echo.
echo Guard App has stopped or failed to start.
pause