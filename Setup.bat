@echo off
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
echo ========================================
echo   ImagePaster Pro - Build System
echo ========================================
echo.

:: التأكد من وجود ملف الأيقونة
if not exist "icon.ico" (
    echo [ERROR] icon.ico file is missing!
    echo Please ensure "icon.ico" exists in this folder.
    pause
    exit /b
)

echo Preparing for build...

:: إغلاق البرنامج إذا كان يعمل
taskkill /f /im ImagePaster.exe /t >nul 2>&1
timeout /t 1 /nobreak >nul

echo Building ImagePaster Pro with integrated icon...
echo Path: icon.ico -> ImagePaster.exe

"%CSC_PATH%" /target:winexe /win32icon:icon.ico /out:ImagePaster.exe Program.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================
    echo   BUILD SUCCESSFUL! 
    echo   Icon has been embedded into EXE
    echo ========================================
    echo.
    echo Starting the application...
    start ImagePaster.exe
    timeout /t 2 /nobreak >nul
) else (
    echo.
    echo ========================================
    echo   BUILD FAILED!
    echo   Check the errors above.
    echo ========================================
    pause
)
