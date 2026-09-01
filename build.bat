@echo off
setlocal

set "PUBLISH_DIR=%~dp0bin\Release\net10.0\win-x64\publish"
set "TARGET_DIR=C:\git\aaa-HachimiDX\src\resources\majdatax"

pushd "%~dp0"
dotnet publish "MajdataEdit-Neo.csproj" --configuration Release --runtime win-x64 --nologo
popd

if errorlevel 1 (
    echo ---
    echo Failed to build the project.
	pause
	exit /b 1
)

REM retry 10 times with 1 second wait
robocopy "%PUBLISH_DIR%" "%TARGET_DIR%" /E /R:10 /W:1
if errorlevel 8 (
    echo ---
	echo Failed to copy files.
	pause
	exit /b 1
)

endlocal
