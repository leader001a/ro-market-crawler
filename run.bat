@echo off
echo Starting RO Market Crawler...
cd /d %~dp0
call venv\Scripts\activate.bat
python -m src.main
pause
