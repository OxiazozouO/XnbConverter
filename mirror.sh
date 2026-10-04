#!/usr/bin/env bash
# 往返镜像验证：unpack → pack → unpack → pack
#   首次解压/压缩可能改变结构；第二次是相对于第一次的，所以必须是不动点。
#   判据：X1 == X2（字节相同），且两轮解出的资源内容相同。
# 用法: mirror.sh <xnb 路径>

D=/c/Users/jiang/.dotnet/dotnet.exe
P=Tests/XnbProf/bin/Release/net8.0/XnbProf.dll
name=$(basename "$1" .xnb)
root=tmp_mir/$name

rm -rf "$root"; mkdir -p "$root/orig"
cp "$1" "$root/orig/"

"$D" "$P" unpack "$root/orig" "$root/a" >/dev/null 2>&1 || { echo "  $name: 首次解包失败"; exit 1; }
"$D" "$P" pack   "$root/a"    "$root/x1" >/dev/null 2>&1 || { echo "  $name: 首次打包失败"; exit 1; }
mkdir -p "$root/in1"; cp "$root/x1"/*.xnb "$root/in1/" 2>/dev/null
"$D" "$P" unpack "$root/in1"  "$root/b"  >/dev/null 2>&1 || { echo "  $name: 二次解包失败"; exit 1; }
"$D" "$P" pack   "$root/b"    "$root/x2" >/dev/null 2>&1 || { echo "  $name: 二次打包失败"; exit 1; }

python - "$root" "$name" <<'EOF'
import sys, os, hashlib
root, name = sys.argv[1], sys.argv[2]
def md5(p): return hashlib.md5(open(p, 'rb').read()).digest()
def listall(d):
    return {f: os.path.join(d, f) for f in os.listdir(d)} if os.path.isdir(d) else {}

def cmp(a, b, label):
    A, B = listall(a), listall(b)
    same = diff = miss = 0
    for k, ap in sorted(A.items()):
        bp = B.get(k)
        if bp is None: miss += 1; continue
        if md5(ap) == md5(bp): same += 1
        else:
            diff += 1
            if diff <= 3: print(f"     不同: {k}")
    print(f"   {label:26} 相同 {same}  不同 {diff}  缺失 {miss}")

cmp(os.path.join(root, 'x1'), os.path.join(root, 'x2'), 'X1 vs X2（必须不动点）')
cmp(os.path.join(root, 'a'), os.path.join(root, 'b'), '两轮解出内容')
orig = listall(os.path.join(root, 'orig')).popitem()[1]
x1 = listall(os.path.join(root, 'x1')).popitem()[1]
print(f"   X1 vs 原始（LZX 不要求相同）: {'相同' if md5(orig) == md5(x1) else '不同'}  {os.path.getsize(orig)} -> {os.path.getsize(x1)}")
EOF
