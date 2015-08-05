@echo off

if "%programfiles(x86)%XXX"=="XXX" goto 32BIT
echo *** 64-bit Windows installed
call "%programfiles(x86)%\Microsoft Visual Studio 10.0\VC\vcvarsall.bat" x86
goto Build

:32BIT
echo *** 32-bit Windows installed
call "%ProgramFiles%\Microsoft Visual Studio 10.0\VC\vcvarsall.bat" x86

:Build
echo.
echo *** Uninstalling service
installutil /u CSender\bin\Debug\CSender.exe

echo.
echo *** Rebuilding project
devenv CSender.sln /Rebuild Debug
IF NOT %ERRORLEVEL%==0 GOTO End

echo.
echo *** Installing service
installutil CSender\bin\Debug\CSender.exe
IF NOT %ERRORLEVEL%==0 GOTO End
sc start CSender

:End
echo.
echo *** Done.
pause
