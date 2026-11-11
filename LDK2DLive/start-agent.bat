@echo off
chcp 65001 >nul
title LDK2D 协作代理
cd /d "%~dp0"

rem 优先用 PATH 里的 node；没有就退回 Unity 项目这台机器上装的那个。
where node >nul 2>nul
if %errorlevel%==0 (
    node agent.mjs
    goto :end
)

if exist "C:\Program Files\nodejs\node.exe" (
    "C:\Program Files\nodejs\node.exe" agent.mjs
    goto :end
)

echo.
echo 没找到 node。装一个 Node.js 再双击这个文件。
echo https://nodejs.org/
echo.

:end
echo.
echo 代理已退出。按任意键关闭。
pause >nul
