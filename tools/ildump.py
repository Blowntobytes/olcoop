#!/usr/bin/env python3
"""Minimal IL dumper: writes one text file per type with each method's IL and resolved tokens.
Usage: dis.py <assembly.dll> <outdir>"""
import sys, os, dnfile
from dncil.cil.body import CilMethodBody
from dncil.cil.error import MethodBodyFormatError
from dncil.clr.token import Token, StringToken, InvalidToken
from dncil.cil.body.reader import CilMethodBodyReaderBase

class R(CilMethodBodyReaderBase):
    def __init__(self, pe, rva):
        self.pe = pe; self.offset = pe.get_offset_from_rva(rva)
    def read(self, n):
        d = self.pe.get_data(self.pe.get_rva_from_offset(self.offset), n); self.offset += n; return d
    def tell(self): return self.offset
    def seek(self, o): self.offset = o; return o

pe = dnfile.dnPE(sys.argv[1]); out = sys.argv[2]; os.makedirs(out, exist_ok=True)
md = pe.net.mdtables
def s(x):
    try: return str(x)
    except Exception: return "?"
typedefs = list(md.TypeDef) if md.TypeDef else []
methods = list(md.MethodDef) if md.MethodDef else []
fields = list(md.Field) if md.Field else []
memberrefs = list(md.MemberRef) if md.MemberRef else []
typerefs = list(md.TypeRef) if md.TypeRef else []
# owner maps
mowner = {}; fowner = {}
for i, t in enumerate(typedefs):
    name = (s(t.TypeNamespace) + "." if s(t.TypeNamespace) else "") + s(t.TypeName)
    for m in t.MethodList: mowner[m.row_index] = name
    for f in t.FieldList: fowner[f.row_index] = name
def tname(row, table):
    if table == "TypeDef":
        return (s(row.TypeNamespace) + "." if s(row.TypeNamespace) else "") + s(row.TypeName)
    if table == "TypeRef":
        return (s(row.TypeNamespace) + "." if s(row.TypeNamespace) else "") + s(row.TypeName)
    return table
def resolve(tok):
    try:
        t = Token(tok); tbl = t.table; rid = t.rid
        if isinstance(tok, int) and (tok >> 24) == 0x70:
            return '"' + pe.net.user_strings.get(tok & 0xffffff).value.replace("\n", "\\n") + '"'
        name = {0x01:"TypeRef",0x02:"TypeDef",0x04:"Field",0x06:"MethodDef",0x0a:"MemberRef",0x1b:"TypeSpec",0x2b:"MethodSpec"}.get(tok >> 24)
        if name == "MethodDef": m = methods[rid-1]; return mowner.get(rid, "?") + "::" + s(m.Name)
        if name == "Field": f = fields[rid-1]; return fowner.get(rid, "?") + "::" + s(f.Name)
        if name == "TypeDef": return tname(typedefs[rid-1], "TypeDef")
        if name == "TypeRef": return tname(typerefs[rid-1], "TypeRef")
        if name == "MemberRef":
            mr = memberrefs[rid-1]; par = mr.Class
            pn = "?"
            try: pn = tname(par.row, par.table.name)
            except Exception: pass
            return pn + "::" + s(mr.Name)
        return "%s#%d" % (name, rid)
    except Exception as e:
        return "tok%08x" % tok if isinstance(tok, int) else s(tok)

byType = {}
for rid, m in enumerate(methods, 1):
    owner = mowner.get(rid, "_"); lines = ["  .method %s  (rid %d)" % (s(m.Name), rid)]
    try:
        p = [s(x.row.Name) for x in m.ParamList] if m.ParamList else []
        lines[0] += "  params(" + ", ".join(p) + ")"
    except Exception: pass
    if m.Rva:
        try:
            body = CilMethodBody(R(pe, m.Rva))
            for ins in body.instructions:
                op = ins.operand
                if isinstance(op, (Token, StringToken, InvalidToken)): opv = resolve(op.value)
                elif hasattr(op, "value") and isinstance(getattr(op, "value"), int) and ins.opcode.name.startswith(("call", "ldfld", "stfld", "ldsfld", "stsfld", "newobj", "ldstr", "ldtoken", "box", "castclass", "isinst", "ldflda", "ldsflda", "newarr", "unbox", "initobj", "ldftn", "ldvirtftn")):
                    opv = resolve(op.value)
                else: opv = s(op) if op is not None else ""
                lines.append("    IL_%04x %s %s" % (ins.offset, ins.opcode.name, opv))
        except Exception as e:
            lines.append("    <err %s>" % e)
    byType.setdefault(owner, []).extend(lines)
for t, ls in byType.items():
    fn = t.replace("/", "_").replace("<", "_").replace(">", "_")[:150]
    with open(os.path.join(out, fn + ".il"), "w") as f: f.write(t + "\n" + "\n".join(ls) + "\n")
print(len(byType), "types")
