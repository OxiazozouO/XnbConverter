#!/usr/bin/env bash
# 按 README 的方式跑 CLI 并计量。
# start /wait 让子进程拿到真实控制台（CLI 进度条要 Console.CursorTop，stdout 重定向会抛异常）。
# 用法: bench.sh "<标签>" "<CLI 命令>" [输出目录]

EXE='D:\XnbConverter\Demos\XnbConverter.Cli\bin\Release\net8.0\XnbConverter.Cli.exe'
ROOT=/d/XnbConverter
TASKLIST=/c/Windows/System32/tasklist.exe
TASKKILL=/c/Windows/System32/taskkill.exe

tag="$1"; cmdline="$2"; outdir="$3"
if [ -n "$outdir" ]; then
  rm -rf "$ROOT/$outdir"; mkdir -p "$ROOT/$outdir"
fi

# 采样当前 CLI 进程的总工作集（KB）
sample_kb() {
  "$TASKLIST" //FI "IMAGENAME eq XnbConverter.Cli.exe" //FO CSV //NH 2>/dev/null \
    | awk -F'","' '{gsub(/[",K ]/,"",$5); s+=$5} END{print s+0}'
}

"$TASKKILL" //F //IM XnbConverter.Cli.exe >/dev/null 2>&1
sleep 0.3

t0=$(date +%s%N)
(cd "$ROOT" && cmd //c start "" //wait //min "$EXE" "$cmdline") &
job=$!

peak=0
while kill -0 $job 2>/dev/null; do
  v=$(sample_kb)
  [ "$v" -gt "$peak" ] 2>/dev/null && peak=$v
  sleep 0.05
done
wait $job
t1=$(date +%s%N)

[ -n "$outdir" ] && files=$(find "$ROOT/$outdir" -type f 2>/dev/null | wc -l) || files="-"

printf '%s\n  用时 %d ms   峰值工作集(采样) %d MB   产出 %s 个文件\n' \
  "$tag" $(( (t1 - t0) / 1000000 )) $(( peak / 1024 )) "$files"
