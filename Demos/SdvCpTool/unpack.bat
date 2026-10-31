@ECHO OFF&chcp 65001>NUL
call "XnbConverter.Cli.exe" "unpack -c -i .\packed -o .\unpacked"
pause