@echo off
set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
echo Compiling and Running...
"%CSC_PATH%" /target:winexe /out:ImagePaster.exe Program.cs
if %ERRORLEVEL% EQU 0 (
    start ImagePaster.exe
) else (
    echo Compilation failed. Check the code for errors.
    pause
)
