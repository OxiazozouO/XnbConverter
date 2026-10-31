"""EditMap 的 FromFile 指向的文件在包里存在吗？（CP 对这一类是"整条跳过"）"""
import os,sys,collections
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
sys.path.insert(0,'D:/SdvCpTool/tools')
import jsonc, cppacks
miss=[]; n=0
for name,d in cppacks.find_cp_packs():
    for dp,_,fns in os.walk(d):
        for f in fns:
            if not f.endswith('.json') or f=='manifest.json': continue
            p=os.path.join(dp,f)
            try: j=jsonc.load(p)
            except Exception: continue
            def walk(x):
                global n
                if isinstance(x,dict):
                    if x.get('Action')=='EditMap' and isinstance(x.get('FromFile'),str):
                        n+=1
                        ff=x['FromFile']
                        if '{{' in ff: return
                        full=os.path.join(d, *ff.split('/'))
                        if not os.path.isfile(full):
                            alt=os.path.join(d,*ff.replace('\','/').split('/'))
                            if not os.path.isfile(alt): miss.append((name[:24], str(x.get('Target'))[:30], ff, bool(x.get('When'))))
                    for v in x.values(): walk(v)
                elif isinstance(x,list):
                    for v in x: walk(v)
            walk(j)
print('EditMap 带 FromFile 的补丁共 %d 条（含带令牌的）；文件找不到的 %d 条：' % (n,len(miss)))
for m in miss[:20]: print('   %-24s %-30s %-46s When=%s' % m)
