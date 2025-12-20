@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

echo ========================================
echo   RO Market Crawler - 배포 빌드 스크립트
echo ========================================
echo.

:: Set version
set VERSION=1.0.0

:: Set paths
set PROJECT_DIR=%~dp0
set PUBLISH_DIR=%PROJECT_DIR%bin\Publish
set DIST_DIR=%PROJECT_DIR%dist\RoMarketCrawler_v%VERSION%

:: Clean previous builds
echo [1/5] 이전 빌드 정리 중...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"

:: Restore and publish
echo [2/5] 프로젝트 빌드 중...
dotnet publish -c Release -o "%PUBLISH_DIR%" --nologo

if %ERRORLEVEL% neq 0 (
    echo.
    echo [오류] 빌드 실패!
    pause
    exit /b 1
)

:: Create distribution folder
echo [3/5] 배포 폴더 생성 중...
mkdir "%DIST_DIR%"

:: Copy main executable
echo [4/5] 파일 복사 중...
copy "%PUBLISH_DIR%\RoMarketCrawler.exe" "%DIST_DIR%\" > nul

:: Copy Data folder
xcopy "%PUBLISH_DIR%\Data" "%DIST_DIR%\Data\" /E /I /Q > nul

:: Copy installation guide (if exists)
if exist "%PROJECT_DIR%INSTALL_GUIDE.txt" (
    copy "%PROJECT_DIR%INSTALL_GUIDE.txt" "%DIST_DIR%\" > nul
)

:: Show results
echo [5/5] 빌드 완료!
echo.
echo ========================================
echo   빌드 결과
echo ========================================
echo.
echo   버전: v%VERSION%
echo   출력 위치: %DIST_DIR%
echo.
echo   포함된 파일:
dir /b "%DIST_DIR%"
echo.

:: Get file size
for %%A in ("%DIST_DIR%\RoMarketCrawler.exe") do set SIZE=%%~zA
set /a SIZE_MB=%SIZE%/1048576
echo   EXE 크기: 약 %SIZE_MB% MB
echo.
echo ========================================
echo.
echo   배포 방법:
echo   1. %DIST_DIR% 폴더를 ZIP으로 압축
echo   2. 사용자에게 ZIP 파일 전달
echo   3. 사용자는 압축 해제 후 RoMarketCrawler.exe 실행
echo.

pause
