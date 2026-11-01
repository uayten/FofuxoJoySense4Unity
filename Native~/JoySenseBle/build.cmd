@echo off
rem Builds JoySenseBle.dll into the package's Runtime/Plugins/x86_64 folder.
rem Needs Visual Studio with the C++ workload and a Windows SDK that ships the C++/WinRT headers.
rem Close the Unity Editor first: once loaded, the DLL stays locked until the Editor exits.
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (
    echo Visual Studio with the C++ workload was not found.
    exit /b 1
)
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>nul || exit /b 1

set "HERE=%~dp0"
set "OUT=%HERE%..\..\Runtime\Plugins\x86_64"
set "OBJ=%HERE%obj"
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OBJ%" mkdir "%OBJ%"

rem /MT links the C++ runtime statically, so players need no Visual C++ redistributable.
cl /nologo /std:c++20 /EHsc /O2 /MT /W4 /permissive- /DNDEBUG /DWIN32_LEAN_AND_MEAN ^
    /Fo"%OBJ%\\" "%HERE%JoySenseBle.cpp" ^
    /LD /link /OUT:"%OUT%\JoySenseBle.dll" /IMPLIB:"%OBJ%\JoySenseBle.lib" windowsapp.lib || exit /b 1
endlocal
