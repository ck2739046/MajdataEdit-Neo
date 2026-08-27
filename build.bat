@echo off
setlocal

pushd "%~dp0"
dotnet publish "MajdataEdit-Neo.csproj" --configuration Release --runtime win-x64 --nologo
set "EXIT_CODE=%ERRORLEVEL%"
popd

if "%EXIT_CODE%"!="0" (
    echo.
    echo Release publish failed with exit code %EXIT_CODE%.
    pause
)

exit /b %EXIT_CODE%
