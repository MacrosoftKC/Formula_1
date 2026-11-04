@echo off
rem Builds the C# and launches the game with the local Godot (.tools\). Pass "editor" to open the editor instead.
cd /d "%~dp0"
dotnet build game\Formula1.csproj -v q || (echo C# build failed & pause & exit /b 1)
set GODOT=.tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
if /i "%1"=="editor" ("%GODOT%" --editor --path game) else ("%GODOT%" --path game)
