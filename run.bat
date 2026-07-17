@echo off
taskkill /F /IM GestionCoutureApp.exe 2>nul
dotnet run --project GestionCoutureApp.csproj
