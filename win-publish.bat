@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 (
    echo Failed to access the AuraSync project directory.
    exit /b 1
)

title AuraSync Build and Packaging Script
echo ======================================================
echo       AuraSync Build and Installer Packaging
echo ======================================================
echo.

where node >nul 2>&1
if errorlevel 1 goto :missing_node
where npm >nul 2>&1
if errorlevel 1 goto :missing_npm
where makensis >nul 2>&1
if errorlevel 1 goto :missing_nsis

echo Cleaning previous generated packages...
if exist "apps\desktop\angular\dist" rmdir /s /q "apps\desktop\angular\dist"
if exist "apps\desktop\electron\release\service" rmdir /s /q "apps\desktop\electron\release\service"
if exist "apps\desktop\electron\release\dist" rmdir /s /q "apps\desktop\electron\release\dist"
if exist "release\AuraSync" rmdir /s /q "release\AuraSync"
if exist "apps\desktop\angular\dist" goto :cleanup_failed
if exist "apps\desktop\electron\release\service" goto :cleanup_failed
if exist "apps\desktop\electron\release\dist" goto :cleanup_failed
if exist "release\AuraSync" goto :cleanup_failed
if not exist "release" mkdir "release"
if errorlevel 1 goto :failed
echo.

echo Restoring npm dependencies...
call npm ci
if errorlevel 1 goto :failed
echo.

echo Building Angular, publishing the .NET service, and packaging Electron...
call npm run package:win
if errorlevel 1 goto :failed
echo.

set "APP_PACKAGE=apps\desktop\electron\release\dist\AuraSync-win32-x64"
if not exist "%APP_PACKAGE%\AuraSync.exe" goto :package_missing

echo Staging the portable AuraSync application...
xcopy /E /I /Y "%APP_PACKAGE%\*" "release\AuraSync\" >nul
if errorlevel 1 goto :failed
if not exist "release\AuraSync\AuraSync.exe" goto :package_missing
echo.

echo Building the NSIS installer...
call makensis "win-installer.nsi"
if errorlevel 1 goto :failed
echo.

echo ======================================================
echo   Build and packaging completed successfully!
echo   Portable app: %CD%\release\AuraSync
echo   Installer:    %CD%\release\AuraSync-Setup-v0.1.0-x64.exe
echo ======================================================
popd
endlocal
exit /b 0

:missing_node
echo Node.js is required but was not found on PATH.
goto :fail_with_message

:missing_npm
echo npm is required but was not found on PATH.
goto :fail_with_message

:missing_nsis
echo NSIS makensis is required but was not found on PATH.
goto :fail_with_message

:cleanup_failed
echo Failed to remove one or more previous generated build outputs.
goto :fail_with_message

:package_missing
echo The packaged AuraSync.exe was not found in the expected Electron output.
goto :fail_with_message

:failed
set "BUILD_ERROR=%ERRORLEVEL%"
if "%BUILD_ERROR%"=="0" set "BUILD_ERROR=1"
echo A build or packaging step failed with exit code %BUILD_ERROR%.
popd
endlocal & exit /b %BUILD_ERROR%

:fail_with_message
popd
endlocal
exit /b 1
