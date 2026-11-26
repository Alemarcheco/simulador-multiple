import math, struct, zlib
from pxr import Usd, UsdGeom, Sdf, UsdShade, Gf, Vt, UsdUtils
# textura 2x1: izquierda roja, derecha azul (PNG a mano)
def png(w,h,rows):
    raw=b''.join(b'\x00'+bytes(r) for r in rows)
    def chunk(t,d): return struct.pack('>I',len(d))+t+d+struct.pack('>I',zlib.crc32(t+d)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw))+chunk(b'IEND',b'')
open('tex0.png','wb').write(png(2,1,[[255,0,0,0,0,255]]))
open('norm0.png','wb').write(png(1,1,[[128,128,255]]))
st=Usd.Stage.CreateNew('pieza.usdc')
UsdGeom.SetStageMetersPerUnit(st,1.0); UsdGeom.SetStageUpAxis(st,'Y')
root=UsdGeom.Xform.Define(st,'/Root'); st.SetDefaultPrim(root.GetPrim())
x=UsdGeom.Xform.Define(st,'/Root/Geom'); x.AddTranslateOp().Set(Gf.Vec3d(0.1,0,0))
m=UsdGeom.Mesh.Define(st,'/Root/Geom/Caja')
# caja 0.04 x 0.02 x 0.03 m subdividida en rejilla para tener muchos índices
pts=[];fv=[];cnt=[];uv=[]
W,H,D=0.04,0.02,0.03
n=6
def face(o,a,b):
    base=len(pts)
    for j in range(n+1):
        for i in range(n+1):
            p=[o[k]+a[k]*i/n+b[k]*j/n for k in range(3)]; pts.append(Gf.Vec3f(*p))
    for j in range(n):
        for i in range(n):
            q=[base+j*(n+1)+i, base+j*(n+1)+i+1, base+(j+1)*(n+1)+i+1, base+(j+1)*(n+1)+i]
            fv.extend(q); cnt.append(4)
            for v in q: uv.append(Gf.Vec2f(0.25 if pts[v][0]<W/2 else 0.75,0.5))
face((0,0,0),(0,0,D),(W,0,0))   # y=0
face((0,H,0),(W,0,0),(0,0,D))   # y=H
face((0,0,0),(W,0,0),(0,H,0))   # z=0
face((0,0,D),(0,H,0),(W,0,0))
face((0,0,0),(0,H,0),(0,0,D))
face((W,0,0),(0,0,D),(0,H,0))
m.CreatePointsAttr(Vt.Vec3fArray(pts)); m.CreateFaceVertexCountsAttr(Vt.IntArray(cnt)); m.CreateFaceVertexIndicesAttr(Vt.IntArray(fv))
pv=UsdGeom.PrimvarsAPI(m).CreatePrimvar('st',Sdf.ValueTypeNames.TexCoord2fArray,UsdGeom.Tokens.faceVarying); pv.Set(Vt.Vec2fArray(uv))
mat=UsdShade.Material.Define(st,'/Root/Mat'); sh=UsdShade.Shader.Define(st,'/Root/Mat/PBR'); sh.CreateIdAttr('UsdPreviewSurface')
t=UsdShade.Shader.Define(st,'/Root/Mat/diffuseColor_texture'); t.CreateIdAttr('UsdUVTexture'); t.CreateInput('file',Sdf.ValueTypeNames.Asset).Set('0/tex0.png')
t2=UsdShade.Shader.Define(st,'/Root/Mat/normal_texture'); t2.CreateIdAttr('UsdUVTexture'); t2.CreateInput('file',Sdf.ValueTypeNames.Asset).Set('0/norm0.png')
sh.CreateInput('diffuseColor',Sdf.ValueTypeNames.Color3f).ConnectToSource(t.ConnectableAPI(),'rgb')
UsdShade.MaterialBindingAPI.Apply(m.GetPrim()).Bind(mat)
st.GetRootLayer().Save()
import os,shutil
os.makedirs('0',exist_ok=True); shutil.copy('tex0.png','0/tex0.png'); shutil.copy('norm0.png','0/norm0.png')
print(UsdUtils.CreateNewUsdzPackage('pieza.usdc','pieza.usdz'))
print(len(pts),len(fv))
