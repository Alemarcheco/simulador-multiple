import math, os, sys, re, io, json, shutil, datetime
sys.path.insert(0, os.path.dirname(__file__))
import py7zr
from data import items

# ---------------------------------------------------------------- malla: lista de (color, [polígonos])
class Mesh:
    def __init__(s): s.solids = []   # (color, [poly]) cada sólido cerrado
    def box(s, x0, x1, y0, y1, z0, z1, c):
        if x1 < x0: x0, x1 = x1, x0
        if y1 < y0: y0, y1 = y1, y0
        if z1 < z0: z0, z1 = z1, z0
        p = [(x0,y0,z0),(x1,y0,z0),(x1,y1,z0),(x0,y1,z0),(x0,y0,z1),(x1,y0,z1),(x1,y1,z1),(x0,y1,z1)]
        f = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        s.solids.append((c, [[p[i] for i in q] for q in f]))
    def cyl_y(s, cx, cz, r, y0, y1, c, seg=20):
        """Cilindro con eje Y (hacia fuera)."""
        if y1 < y0: y0, y1 = y1, y0
        a = [2*math.pi*i/seg for i in range(seg)]
        b = [(cx+r*math.cos(t), y0, cz+r*math.sin(t)) for t in a]
        t_ = [(cx+r*math.cos(t), y1, cz+r*math.sin(t)) for t in a]
        polys = [list(reversed(b)) if False else b[::-1], t_]
        # orientar: tapa y0 mira a -Y, tapa y1 a +Y
        polys = [b, t_[::-1]]
        for i in range(seg):
            j = (i+1) % seg
            polys.append([b[i], t_[i], t_[j], b[j]])
        s.solids.append((c, polys))
    def cyl_z(s, cx, cy, r, z0, z1, c, seg=16):
        a = [2*math.pi*i/seg for i in range(seg)]
        b = [(cx+r*math.cos(t), cy+r*math.sin(t), z0) for t in a]
        t_ = [(cx+r*math.cos(t), cy+r*math.sin(t), z1) for t in a]
        polys = [b[::-1], t_]
        for i in range(seg):
            j = (i+1) % seg
            polys.append([b[i], b[j], t_[j], t_[i]])
        s.solids.append((c, polys))

GREY=0xFFE4E6E8; DGREY=0xFF4A5058; BLACK=0xFF2B2F33; DARK=0xFF1E2226; BLUE=0xFF2F6DB5; ORANGE=0xFFE87722
SCREW=0xFFB9C0C7; GREEN=0xFF39C24A; COPPER=0xFFC8803C; SCREEN=0xFF203040; WHITE=0xFFF2F2F2; RED=0xFFD32F2F; YEL=0xFFE8C21F

def screws_row(m, x0, x1, n, z, ytop, r=1.6, c=SCREW):
    for i in range(n):
        x = x0 + (i+0.5)*(x1-x0)/n
        m.cyl_y(x, z, r, ytop-0.5, ytop+0.3, c, 10)

def din_clip(m, W, H):
    m.box(W*0.15, W*0.85, 0, 3, H*0.5-20, H*0.5+20, DGREY)

def build(it):
    m = Mesh(); W, H, D = it['W'], it['H'], it['D']; fam = it['family']
    if fam == 'slimrelay':
        # Base con bornes arriba y abajo, relé encima en el centro.
        m.box(0, W, 0, D*0.55, 0, H, GREY)
        m.box(0.3, W-0.3, D*0.55, D*0.95, H*0.28, H*0.72, BLUE if 'U1' in it['ref'] else DGREY)
        m.box(0.6, W-0.6, D*0.95, D, H*0.33, H*0.67, DGREY)
        for z in (H*0.12, H*0.88): m.cyl_y(W/2, z, min(2, W/2-0.6), D*0.55-0.5, D*0.55+0.3, SCREW, 10)
        m.box(0.8, W-0.8, D*0.55, D*0.56, H*0.2, H*0.24, 0xFFFFC107)  # palanca/LED
    elif fam == 'mcb':
        p = it.get('poles', 1); pw = W/p
        m.box(0, W, 0, D*0.62, 0, H, GREY)
        m.box(0, W, D*0.62, D, H*0.27, H*0.73, GREY)
        for i in range(p):
            x = i*pw + pw/2
            m.box(x-4, x+4, D, D+8, H*0.40, H*0.55, BLACK)    # maneta
            m.box(x-5, x+5, D, D+0.5, H*0.6, H*0.68, WHITE)  # ventana de estado
            for z in (H*0.1, H*0.9): m.cyl_y(x, z, 2.4, D*0.62-0.4, D*0.62+0.3, SCREW, 12)
        m.box(0, W, -0.01, 0.01, 0, 0, GREY)  # (sin efecto)
        din_clip(m, W, H)
    elif fam in ('barrier', 'slimmod'):
        m.box(0, W, 0, D*0.8, 0, H, it.get('ifront', DGREY) if fam=='barrier' else GREY)
        m.box(0.3, W-0.3, D*0.8, D, H*0.18, H*0.82, it.get('ifront', DGREY) if fam=='barrier' else GREY)
        # Bloques de bornes enchufables (arriba y abajo)
        tb = BLUE if fam=='barrier' else 0xFF2B6E3F
        m.box(0.2, W-0.2, D*0.45, D*0.8+0.6, 0.4, H*0.16, tb if fam=='barrier' else DGREY)
        m.box(0.2, W-0.2, D*0.45, D*0.8+0.6, H*0.84, H-0.4, tb if fam=='barrier' else DGREY)
        for z in (H*0.05, H*0.11, H*0.89, H*0.95): m.cyl_y(W/2, z, min(1.6, W/2-0.6), D*0.8, D*0.8+0.9, SCREW, 10)
        m.box(W*0.25, W*0.75, D, D+0.4, H*0.7, H*0.73, GREEN)
        m.box(W*0.15, W*0.85, D, D+0.3, H*0.3, H*0.55, WHITE)  # etiqueta
    elif fam == 'chassis':
        n = it['slots']; t = 2
        m.box(0, W, 0, 4, 0, H, 0xFF9AA2AA)           # espalda (backplane)
        m.box(0, t, 0, D*0.35, 0, H, 0xFF9AA2AA)
        m.box(W-t, W, 0, D*0.35, 0, H, 0xFF9AA2AA)
        m.box(0, W, 0, D*0.35, 0, 6, 0xFF9AA2AA)
        m.box(0, W, 0, D*0.35, H-6, H, 0xFF9AA2AA)
        pitch = (W-2*t-8)/n
        for i in range(n):
            x = t+4+i*pitch
            m.box(x+pitch*0.3, x+pitch*0.7, 4, 8, H*0.3, H*0.7, DARK)   # conector del backplane
        for z in (8, H-8): m.cyl_y(-6, z, 2.5, 0, 3, SCREW)  # orejas de fijación
        m.box(-12, 0, 0, 3, 0, H, 0xFF9AA2AA)
        m.box(W, W+12, 0, 3, 0, H, 0xFF9AA2AA)
    elif fam == 'module1756':
        z0, z1 = (169-140)/2, (169+140)/2
        m.box(0.5, W-0.5, 0, D-4, z0, z1, DARK)
        m.box(1.5, W-1.5, D-4, D, z0+5, z1-5, 0xFF3A3F45)   # puerta frontal
        m.box(4, W-4, D, D+0.4, z1-40, z1-12, WHITE)       # etiqueta
        leds = it.get('leds', 1)
        rows = min(leds, 16)
        for i in range(min(rows, 8)):
            m.box(5, 9, D, D+0.6, z1-50-i*3.2, z1-48-i*3.2, GREEN)
            if leds > 8: m.box(W-9, W-5, D, D+0.6, z1-50-i*3.2, z1-48-i*3.2, GREEN)
        if it.get('display'): m.box(6, W-6, D, D+0.5, z1-22, z1-14, 0xFF101820)
        m.box(8, W-8, D, D+3, z0+8, z0+14, BLACK)  # tirador
    elif fam == 'bigps':
        m.box(0, W, 0, D, 0, H, 0xFF2B2F33)
        m.box(4, W-4, D, D+0.4, H-40, H-10, WHITE)
        m.box(W*0.15, W*0.85, D, D+2, H*0.15, H*0.35, 0xFF15181B)   # regleta frontal
        m.box(W*0.1, W*0.2, D, D+0.6, H-55, H-50, GREEN)
        for i in range(8): m.box(W*0.3, W*0.7, D, D+0.3, H*0.4+i*5, H*0.4+i*5+2, 0xFF3C434A)  # rejilla
    elif fam in ('dynamix', 'tbase'):
        base = 35.7
        m.box(0, W, 0, base, 0, H, 0xFF3A3F45)
        for z in (H*0.06, H*0.94):
            screws_row(m, 2, W-2, int(W//6), z, base)
        if fam == 'dynamix':
            m.box(2, W-2, base, D, H*0.13, H*0.87, 0xFF2B2F33)
            m.box(5, W-5, D, D+0.4, H*0.6, H*0.82, WHITE)
            for i in range(it.get('leds', 4)): m.box(5+i*5, 8+i*5, D, D+0.6, H*0.5, H*0.52, GREEN)
            m.box(W/2-6, W/2+6, D, D+1, H*0.2, H*0.28, 0xFF101820)  # RJ45
        din_clip(m, W, H)
    elif fam == 'rtcnet':
        m.box(0, W, 0, D, 0, H, 0xFFA7ADB3)      # caja metálica
        m.box(3, W-3, D, D+0.4, H*0.55, H*0.85, WHITE)
        for side in (0, 1):  # conectores enchufables arriba y abajo
            z0 = H-14 if side == 0 else 0
            m.box(8, W-8, D*0.2, D+10, z0, z0+14, 0xFF2B6E3F)
            screws_row(m, 10, W-10, 10, z0+7, D+10, 1.2)
        for i in range(4): m.box(6+i*6, 9+i*6, D, D+0.6, H*0.45, H*0.47, GREEN)
        din_clip(m, W, H)
    elif fam == 'boxpc':
        m.box(0, W, 0, D, 0, H, 0xFF707880)
        for i in range(int(W//8)): m.box(i*8+2, i*8+5, D, D+4, 6, H-6, 0xFF5A6068)  # aletas
        m.box(-10, 0, 0, 2, 10, H-10, 0xFF5A6068); m.box(W, W+10, 0, 2, 10, H-10, 0xFF5A6068)
    elif fam == 'proximitor':
        m.box(0, W, 0, D, 0, H, 0xFF3A3F45)
        m.box(4, W-4, D, D+0.4, H*0.3, H*0.7, WHITE)
        m.cyl_z(W/2, D/2, 6, H, H+12, 0xFFB0B5BA)   # conector del cable de extensión
        screws_row(m, 4, W-4, 5, 6, D, 1.4)
        din_clip(m, W, H)
    elif fam == 'busbar':
        for x in (10, W-10):
            m.box(x-10, x+10, 0, 25, H*0.15, H*0.85, 0xFF2B2F33)   # aisladores
        m.box(0, W, 25, 32, 0, H, COPPER)
        for i in range(12):
            x = 25 + i*(W-50)/11
            for z in (H*0.3, H*0.7): m.cyl_y(x, z, 2.6, 31.7, 32.3, 0xFF5A3A1C, 10)
    elif fam == 'doorswitch':
        m.box(0, W, 0, D*0.8, 0, H, DGREY)
        m.box(W*0.3, W*0.7, D*0.8, D, H*0.3, H*0.7, 0xFFC0C4C8)   # pulsador
    elif fam == 'serialsrv':
        m.box(0, W, 0, D, 0, H, 0xFF2B2F33)
        m.box(W*0.3, W*0.7, D*0.3, D*0.8, H, H+6, 0xFFB0B5BA)   # DB9
        m.box(W*0.1, W*0.5, D*0.2, D*0.8, -4, 0, 0xFF2B6E3F)   # bornes RS485
        m.box(W*0.6, W*0.9, D*0.2, D*0.8, -4, 0, 0xFFB0B5BA)   # RJ45
        m.box(5, 25, D, D+0.4, H*0.6, H*0.85, WHITE)
    # ---------------- puerta: Y=0 en la cara exterior de la puerta, Y>0 sobresale, Y<0 por detrás
    elif fam in ('door_pilot', 'door_button', 'door_selector', 'door_estop'):
        cx, cz = W/2, H/2
        m.box(cx-14, cx+14, -D, -D+40, cz-14, cz+14, 0xFF6A7077)      # bloque de contactos 800T-XA
        m.box(cx-17, cx+17, -D+40, 0, cz-17, cz+17, 0xFF4A5058)        # cuerpo/adaptador
        m.cyl_y(cx, cz, 30.5/2, -3, 0, 0xFF8C9298)                       # rosca en el taladro
        if fam == 'door_estop':
            m.box(0, W, 0, 1, 0, H, YEL)                                   # placa amarilla
            m.cyl_y(cx, cz, 16, 1, 18, BLACK)
            m.cyl_y(cx, cz, 22.5, 18, 32, RED, 28)
        else:
            m.cyl_y(cx, cz, W/2, 0, 6, 0xFFB9C0C7, 28)                    # bisel metálico
            col = it.get('color', BLACK)
            if fam == 'door_pilot': m.cyl_y(cx, cz, 13, 6, 16, col, 24)
            elif fam == 'door_button':
                m.cyl_y(cx, cz, 13, 6, 10 if not it.get('lit') else 14, col, 24)
            else:
                m.cyl_y(cx, cz, 14, 6, 9, BLACK, 24)
                m.box(cx-4, cx+4, 9, 24, cz-13, cz+13, DARK)               # maneta
    elif fam in ('hmi_ctrl', 'monitor', 'meter'):
        bez = 6 if fam != 'meter' else 3
        m.box(0, W, 0, bez, 0, H, BLACK)                                    # marco frontal
        m.box(W*0.06, W*0.94, -D+bez, 0, H*0.06, H*0.94, 0xFF3A4652)        # cuerpo detrás de la puerta
        if fam == 'monitor':
            m.box(W*0.05, W*0.95, bez, bez+0.5, H*0.06, H*0.94, SCREEN)
        elif fam == 'meter':
            m.box(W*0.08, W*0.72, bez, bez+0.5, H*0.25, H*0.8, 0xFFB02020)   # dígitos LED
            for i in range(4): m.box(W*0.76, W*0.92, bez, bez+2, H*(0.15+i*0.2), H*(0.15+i*0.2)+H*0.12, 0xFF5A6068)
        else:
            m.box(W*0.06, W*0.62, bez, bez+0.5, H*0.32, H*0.92, SCREEN)      # pantalla
            if it.get('keypad', True):
                for r in range(4):
                    for c in range(3):
                        x = W*0.68 + c*W*0.09; z = H*0.35 + r*H*0.14
                        m.box(x, x+W*0.07, bez, bez+3, z, z+H*0.1, 0xFF5C6670)
            for i in range(6):
                x = W*0.06 + i*W*0.093
                m.box(x, x+W*0.08, bez, bez+3, H*0.1, H*0.22, 0xFF5C6670)
    else:
        raise ValueError(fam)
    return m

# ---------------------------------------------------------------- escritores
def hexc(c): return f"{c & 0xFFFFFF:06X}"

def write_obj(m, name):
    obj = io.StringIO(); mtl = io.StringIO()
    obj.write(f"# Modelo 3D de {name} para Simulador multiple. Unidades: mm. X ancho, Y fondo (hacia fuera), Z alto.\n")
    obj.write(f"mtllib {name}.mtl\n")
    cols = sorted({c for c, _ in m.solids})
    for c in cols:
        r, g, b = ((c >> 16) & 255)/255, ((c >> 8) & 255)/255, (c & 255)/255
        mtl.write(f"newmtl c_{hexc(c)}\nKd {r:.3f} {g:.3f} {b:.3f}\n\n")
    vi = 1
    for k, (c, polys) in enumerate(m.solids):
        obj.write(f"o parte{k+1}\nusemtl c_{hexc(c)}\n")
        for poly in polys:
            for p in poly: obj.write("v %.3f %.3f %.3f\n" % p)
            obj.write("f " + " ".join(str(vi+i) for i in range(len(poly))) + "\n")
            vi += len(poly)
    return obj.getvalue(), mtl.getvalue()

def write_step(m, name, desc):
    L = []; n = [0]
    def e(s): n[0] += 1; L.append(f"#{n[0]}={s};"); return n[0]
    now = datetime.datetime(2026, 11, 26).strftime("%Y-%m-%dT%H:%M:%S")
    ctx_u = e("( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) )")
    ang = e("( NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.) )")
    sol = e("( NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT() )")
    unc = e(f"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-03),#{ctx_u},'distance_accuracy_value','')")
    ctx = e(f"( GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{unc})) GLOBAL_UNIT_ASSIGNED_CONTEXT((#{ctx_u},#{ang},#{sol})) REPRESENTATION_CONTEXT('','') )")
    app = e("APPLICATION_CONTEXT('automotive design')")
    e(f"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,#{app})")
    pc = e(f"PRODUCT_CONTEXT('',#{app},'mechanical')")
    prod = e(f"PRODUCT('{name}','{name}','{desc}',(#{pc}))")
    pdfw = e(f"PRODUCT_DEFINITION_FORMATION('','',#{prod})")
    pdc = e(f"PRODUCT_DEFINITION_CONTEXT('part definition',#{app},'design')")
    pd = e(f"PRODUCT_DEFINITION('design','',#{pdfw},#{pdc})")
    pds = e(f"PRODUCT_DEFINITION_SHAPE('','',#{pd})")
    breps = []
    for c, polys in m.solids:
        faces = []
        for poly in polys:
            pts = [e("CARTESIAN_POINT('',(%.3f,%.3f,%.3f))" % p) for p in poly]
            loop = e("POLY_LOOP('',(" + ",".join(f"#{i}" for i in pts) + "))")
            fb = e(f"FACE_OUTER_BOUND('',#{loop},.T.)")
            faces.append(e(f"FACE('',(#{fb}))"))
        shell = e("CLOSED_SHELL('',(" + ",".join(f"#{i}" for i in faces) + "))")
        breps.append(e(f"FACETED_BREP('',#{shell})"))
    origin = e("CARTESIAN_POINT('',(0.,0.,0.))")
    ax = e(f"AXIS2_PLACEMENT_3D('',#{origin},$,$)")
    rep = e("FACETED_BREP_SHAPE_REPRESENTATION('',(" + ",".join(f"#{i}" for i in breps) + f",#{ax}),#{ctx})")
    e(f"SHAPE_DEFINITION_REPRESENTATION(#{pds},#{rep})")
    head = ("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('Simulador multiple: modelo 3D facetado'),'2;1');\n"
            f"FILE_NAME('{name}.stp','{now}',(''),(''),'Simulador multiple','Simulador multiple','');\n"
            "FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\nENDSEC;\nDATA;\n")
    return head + "\n".join(L) + "\nENDSEC;\nEND-ISO-10303-21;\n"

def xesc(s): return (s.replace("&", "&amp;").replace('"', "&quot;").replace("<", "&lt;").replace(">", "&gt;"))

def write_xml(it, fname):
    partnr = f"{it['brand']}.{it['ref']}"
    a = dict(P_ARTICLE_PARTNR=partnr, P_ARTICLE_MANUFACTURER=it['brand'], P_ARTICLE_TYPENR=it['ref'],
             P_ARTICLE_ORDERNR=it['ref'], P_ARTICLE_DESCR1=it['desc'], P_ARTICLE_WIDTH=f"{it['W']:g}",
             P_ARTICLE_HEIGHT=f"{it['H']:g}", P_ARTICLE_DEPTH=f"{it['D']:g}", P_ARTICLE_POWER_DISSIPATION=f"{it['heat']:g}",
             P_ARTICLE_MOUNTINGSITE=("Puerta" if it['loc'] == 'Door' else "Placa de montaje"),
             P_ARTICLE_MOUNTINGSPACE=("Carril DIN 35 mm" if it['rail'] else "Atornillado"),
             P_ARTICLE_NOTE=it['source'])
    s = ['<?xml version="1.0" encoding="utf-8"?>',
         '<!-- Datos de pieza con las propiedades P_ARTICLE_* de EPLAN. Generado para Simulador multiple. -->',
         '<partsmanagement>', '  <part ' + " ".join(f'{k}="{xesc(v)}"' for k, v in a.items()) + '>']
    sim = dict(kind=it['kind'], location=it['loc'], onRail=str(it['rail']).lower(), verified=str(it['verified']).lower(),
               source=it['source'], model3d=f"3D/{fname}.obj", step=f"3D/{fname}.stp", keys=";".join(it['keys']),
               excelRows=";".join(str(r) for r in it['rows']))
    s.append('    <simulador ' + " ".join(f'{k}="{xesc(v)}"' for k, v in sim.items()) + ' />')
    s += ['  </part>', '</partsmanagement>', '']
    return "\n".join(s)

def safe(s):
    import unicodedata
    s = unicodedata.normalize("NFKD", s.replace("ü", "ue")).encode("ascii", "ignore").decode()
    return re.sub(r"[^A-Za-z0-9.+-]+", "_", s).strip("_")

def main(out):
    if os.path.exists(out): shutil.rmtree(out)
    os.makedirs(out)
    for it in items:
        fname = safe(f"{it['brand']}_{it['ref']}")
        m = build(it)
        # quitar sólidos degenerados
        m.solids = [(c, p) for c, p in m.solids if all(len(set(q)) == len(q) for q in p)]
        obj, mtl = write_obj(m, fname)
        stp = write_step(m, fname, xesc(it['desc']).replace("'", " "))
        xml = write_xml(it, fname)
        path = os.path.join(out, fname + ".edz")
        with py7zr.SevenZipFile(path, 'w') as z:
            z.writestr(xml, f"items/{fname}.xml")
            z.writestr(obj, f"3D/{fname}.obj")
            z.writestr(mtl, f"3D/{fname}.mtl")
            z.writestr(stp, f"3D/{fname}.stp")
        print(fname, len(m.solids), os.path.getsize(path))

if __name__ == "__main__":
    main(sys.argv[1])
