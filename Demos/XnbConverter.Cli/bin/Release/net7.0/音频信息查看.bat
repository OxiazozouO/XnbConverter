@ECHO OFF&SETLOCAL ENABLEDELAYEDEXPANSION
set ffprobe="%ffprobe%"
if [%1]==[] (
    goto :Scanf
) else (
    set pp=%1
    goto :Menu
)
:Scanf
set /p pp=input:
:Menu
for %%z in (!pp!) do if "%%~az" geq "d" (
    cd !pp!
    for /r %%2 in (*) do (set j="%%2"
        echo "!ffprobe! -v quiet -show_format -show_streams -print_format json !j!"
        call !ffprobe! -i -show_format -show_entries streams -v quiet -of flat !j!
    )
) else if "%%~az" geq "-" (
    echo !pp!
    call !ffprobe! -v quiet -show_format -show_streams -print_format json !pp!
) else (
    echo "err : Inaccessible"
)
echo.
goto :Scanf
pause