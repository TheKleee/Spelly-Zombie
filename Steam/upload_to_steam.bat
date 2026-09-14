@echo off
rem Uploads the build in D:\Games\Unity Builds to Steam (app 5050950).
rem Uploading never releases the game and never shows the store page.
setlocal
set "SDK=D:\SteamSDK\sdk\tools\ContentBuilder"
set "BUILD=D:\Games\Unity Builds"

if not exist "%SDK%\builder\steamcmd.exe" (
  echo The Steamworks SDK is not at %SDK%
  echo Unzip the SDK to D:\SteamSDK first.
  pause
  exit /b 1
)
if not exist "%BUILD%\Spelly Zombie.exe" (
  echo There is no build in %BUILD%
  pause
  exit /b 1
)

echo Copying the build without the DoNotShip folder...
robocopy "%BUILD%" "%SDK%\content" /MIR /XD "Spelly Zombie_BurstDebugInformation_DoNotShip" /NFL /NDL /NJH /NP
if errorlevel 8 (
  echo The copy failed.
  pause
  exit /b 1
)
copy /y "%~dp0app_build_5050950.vdf" "%SDK%\scripts\app_build_5050950.vdf" >nul

set /p "LOGIN=Your Steam login name: "
cd /d "%SDK%\builder"
steamcmd.exe +login %LOGIN% +run_app_build "%SDK%\scripts\app_build_5050950.vdf" +quit
echo.
echo If it says the build finished with a BuildID, set it live on the default branch in Steamworks, SteamPipe, Builds.
pause
