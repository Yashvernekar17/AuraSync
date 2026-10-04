@echo off
setlocal
title AuraSync Development
pushd "%~dp0"

if not defined NODE_HOME if exist "%ProgramFiles%\nodejs\node.exe" set "NODE_HOME=%ProgramFiles%\nodejs"
if defined NODE_HOME set "PATH=%NODE_HOME%;%PATH%"

where node >nul 2>&1
if errorlevel 1 (
    echo ERROR: Node.js is required to run AuraSync.
    goto :failed
)

where npm >nul 2>&1
if errorlevel 1 (
    echo ERROR: npm is required to run AuraSync.
    goto :failed
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: The .NET 10 SDK is required to run the AuraSync service.
    goto :failed
)

dotnet --list-sdks | findstr /R "^10\." >nul
if errorlevel 1 (
    echo ERROR: The .NET 10 SDK is required to run the AuraSync service.
    goto :failed
)

if not exist "node_modules" (
    echo Installing workspace dependencies...
    call npm install
    if errorlevel 1 goto :failed
)

echo Starting AuraSync development services.
echo Angular, Electron, and the .NET API will stop when this window is closed.
echo.
call npm run dev:desktop
set "exitCode=%errorlevel%"
popd
exit /b %exitCode%

:failed
set "exitCode=%errorlevel%"
if "%exitCode%"=="0" set "exitCode=1"
popd
echo.
echo AuraSync did not start. Check the requirements and error above.
pause
exit /b %exitCode%
