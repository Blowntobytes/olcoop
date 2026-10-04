# Static scan of Overload campaign scenes (read-only, local game files; output never committed).
# pip install UnityPy TypeTreeGeneratorAPI; stage Overload_Data/globalgamemanagers.assets, Managed/ and the levelN files,
# then: python3 tools/levelscan.py   (D below = folder holding them). Prints objective type and every security key with
# NetworkIdentity, file active state, parent chain and the scripts that reference it.
import UnityPy, sys, collections, json
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
import os
D=os.environ.get("OVERLOAD_DATA","/mnt/user-data/uploads/Overload/Overload_Data/")
gen=TypeTreeGenerator("2017.4.10f1"); gen.load_local_dll_folder(D+'Managed')
NODES={}
def nodes(sc):
    k=sc.m_ClassName
    if k not in NODES:
        full=(sc.m_Namespace+'.' if sc.m_Namespace else '')+k
        try: NODES[k]=gen.get_nodes_up(sc.m_AssemblyName.replace('.dll',''), full)
        except: NODES[k]=None
    return NODES[k]
SCN={5:'sp_outer_02',7:'sp_outer_03',8:'sp_outer_04',11:'sp_titan_06',12:'sp_titan_07',13:'sp_titan_08',14:'sp_titan_09',21:'sp_outer_05',22:'sp_outer_01',25:'sp_secret_01',26:'sp_titan_10',27:'sp_inner_11',28:'sp_inner_12',29:'sp_alien_14',30:'sp_alien_13',31:'sp_alien_15',32:'sp_alien_16'}
def ptrs(v,acc):
    if isinstance(v,dict):
        if 'm_PathID' in v and 'm_FileID' in v:
            if v['m_FileID']==0 and v['m_PathID']: acc.append(v['m_PathID'])
        else:
            for x in v.values(): ptrs(x,acc)
    elif isinstance(v,list):
        for x in v: ptrs(x,acc)
for idx in sorted(SCN, key=lambda i: SCN[i]):
    env=UnityPy.load(D+f'level{idx}', D+'globalgamemanagers.assets')
    go={}; tr={}; trgo={}; comps=collections.defaultdict(list)
    for o in env.objects:
        if o.type.name=='GameObject': g=o.read(); go[o.path_id]=(g.m_Name,g.m_IsActive)
        elif o.type.name=='Transform': t=o.read(); tr[t.m_GameObject.path_id]=t.m_Father.path_id; trgo[o.path_id]=t.m_GameObject.path_id
    obj=0; keys=[]; netid=set(); refs=collections.defaultdict(list); robots=0
    for o in env.objects:
        if o.type.name!='MonoBehaviour': continue
        try: mb=o.read(check_read=False); sc=mb.m_Script.read(); cn=sc.m_ClassName
        except: continue
        gid=mb.m_GameObject.path_id
        if cn=='NetworkIdentity': netid.add(gid); continue
        if cn=='Robot': robots+=1; continue
        if cn not in ('Item','LevelCustomInfo') and not cn.startswith('Script') and not cn.startswith('Trigger'): continue
        n=nodes(sc)
        if not n: continue
        try: t=o.read_typetree(n, check_read=False)
        except: continue
        if cn=='Item' and t.get('m_type')==25: keys.append(gid)
        if cn=='LevelCustomInfo': obj=t.get('m_objective')
        if cn.startswith('Script') or cn.startswith('Trigger'):
            acc=[]; ptrs(t,acc)
            for p in acc: refs[p].append(cn)
    def parents(g):
        out=[]; f=tr.get(g)
        while f:
            pg=trgo.get(f); out.append(go.get(pg,('?',None))); f=tr.get(pg)
        return out
    print(f"{SCN[idx]:13} objective={obj} keys={len(keys)}")
    for k in keys:
        par=parents(k)
        print(f"     key#{k} netid={k in netid} inactive_in_file={not go[k][1]} parents={'/'.join(p[0] for p in reversed(par))} parents_all_active={all(p[1] for p in par)} watched_by={sorted(set(refs.get(k,[])))}")
