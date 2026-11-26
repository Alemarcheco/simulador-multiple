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

def _orient(polys):
    """Gira cada cara para que su normal apunte hacia fuera del sólido (sólidos convexos)."""
    pts = [p for q in polys for p in q]
    cx = sum(p[0] for p in pts)/len(pts); cy = sum(p[1] for p in pts)/len(pts); cz = sum(p[2] for p in pts)/len(pts)
    out = []
    for q in polys:
        nx = ny = nz = 0.0
        for i in range(len(q)):
            a, b = q[i], q[(i+1) % len(q)]
            nx += (a[1]-b[1])*(a[2]+b[2]); ny += (a[2]-b[2])*(a[0]+b[0]); nz += (a[0]-b[0])*(a[1]+b[1])
        fx = sum(p[0] for p in q)/len(q)-cx; fy = sum(p[1] for p in q)/len(q)-cy; fz = sum(p[2] for p in q)/len(q)-cz
        out.append(q if nx*fx+ny*fy+nz*fz >= 0 else q[::-1])
    return out

def prism_x(m, x0, x1, prof, c):
    """Prisma a lo ancho (X) con perfil convexo [(y, z), ...] en el plano Y-Z."""
    a = [(x0, y, z) for y, z in prof]; b = [(x1, y, z) for y, z in prof]
    polys = [a, b]
    for i in range(len(prof)):
        j = (i+1) % len(prof)
        polys.append([a[i], a[j], b[j], b[i]])
    m.solids.append((c, _orient(polys)))

# Colores tomados de las fotos oficiales de cada fabricante (sin logotipos)
LGREY=0xFFD9DCDF; RA_GREY=0xFFC9CDD1; ALU=0xFFB8BEC4; ALU_D=0xFF959CA3; TEAL=0xFF33A79C; PF_BLUE=0xFF2F6DB5
TB_GREEN=0xFF2FA84F; TB_BLACK=0xFF23272B; HOLE=0xFF101214; LED_G=0xFF4CD964; LED_R=0xFFE53935; LED_Y=0xFFFFC107
WE_ORANGE=0xFFEE7F00; WE_BEIGE=0xFFE6E1D3; BN_BLUE=0xFF1F5FAF; PD_BLUE=0xFF2157A8; LCD=0xFF9DB39A; LCD_D=0xFF2A3A2A
KEY=0xFF5C6670; KEY_L=0xFFC4C9CE

def plug_tb(m, x0, x1, y0, y1, z0, z1, n, col, top=True):
    """Bloque de bornes enchufable: cuerpo, tornillos al frente y entradas de cable en la cara exterior."""
    m.box(x0, x1, y0, y1, z0, z1, col)
    pitch = (x1-x0)/n; r = min(1.4, pitch*0.3)
    for i in range(n):
        x = x0 + (i+0.5)*pitch
        m.cyl_y(x, (z0+z1)/2 + (z1-z0)*0.15*(1 if top else -1), r, y1-0.3, y1+0.4, SCREW, 10)
        ze = z1 if top else z0
        m.box(x-pitch*0.28, x+pitch*0.28, y0+(y1-y0)*0.35, y1-(y1-y0)*0.2, ze-0.2 if top else ze-0.1, ze+0.1 if top else ze+0.2, HOLE)

def leds(m, x0, z, y, n, step, col=LED_G, w=2.4, h=1.6, vertical=True):
    for i in range(n):
        if vertical: m.box(x0, x0+w, y, y+0.5, z-i*step, z-i*step+h, col)
        else: m.box(x0+i*step, x0+i*step+w, y, y+0.5, z, z+h, col)

def build(it):
    m = Mesh(); W, H, D = it['W'], it['H'], it['D']; fam = it['family']
    if fam == 'slimrelay':
        # 700-HL: zócalo gris con bornes de tornillo a los dos extremos y relé enchufado en el centro.
        m.box(0, W, 0, D*0.5, 0, H, RA_GREY)
        prism_x(m, 0, W, [(D*0.5, H*0.17), (D*0.62, H*0.17), (D*0.62, H*0.83), (D*0.5, H*0.83)], RA_GREY)
        m.box(0.25, W-0.25, D*0.62, D*0.97, H*0.27, H*0.73, 0xFF2E3338)        # relé
        m.box(0.5, W-0.5, D*0.97, D, H*0.3, H*0.70, 0xFF3A4046)
        m.box(0.6, W-0.6, D*0.97, D+0.3, H*0.62, H*0.67, WHITE)               # etiqueta del relé
        m.box(W*0.3, W*0.7, D*0.62, D*0.9, H*0.74, H*0.8, 0xFF6A7077)         # palanca de extracción
        for z in (H*0.06, H*0.94):
            m.cyl_y(W/2, z, min(1.9, W/2-0.5), D*0.5-0.4, D*0.5+0.3, SCREW, 10)
            m.box(W*0.2, W*0.8, D*0.15, D*0.4, H if z > H/2 else -0.1, H+0.1 if z > H/2 else 0, HOLE)
        m.box(W*0.25, W*0.75, D*0.62, D*0.63, H*0.2, H*0.23, LED_Y)            # LED
        for z in (H*0.12, H*0.88): m.box(0.3, W-0.3, D*0.5, D*0.5+0.3, z-2, z+2, WHITE)  # marcadores
    elif fam == 'mcb':
        # ABB S200: gris claro RAL 7035, maneta gris oscura, ventana de posición roja/verde, frente de 45 mm.
        p = it.get('poles', 1); pw = W/p; body = 0xFFE8E9E6
        m.box(0, W, 0, D*0.6, 0, H, body)
        prism_x(m, 0, W, [(D*0.6, H*0.22), (D, H*0.25), (D, H*0.75), (D*0.6, H*0.78)], body)
        for i in range(p):
            x = i*pw + pw/2
            m.box(x-3.5, x+3.5, D, D+9, H*0.42, H*0.56, 0xFF3A3F45)             # maneta
            m.box(x-3.8, x+3.8, D+9, D+10, H*0.42, H*0.6, 0xFF3A3F45)
            m.box(x-2.5, x+2.5, D, D+0.4, H*0.63, H*0.67, LED_R if i == 0 else 0xFFE8E9E6)  # indicador
            m.box(x-6.5, x+6.5, D, D+0.3, H*0.27, H*0.38, 0xFFF6F6F4)          # rotulación
            for z in (H*0.08, H*0.92):
                m.cyl_y(x, z, 2.6, D*0.6-0.4, D*0.6+0.3, SCREW, 12)
                m.box(x-4, x+4, D*0.2, D*0.5, H if z > H/2 else -0.1, H+0.1 if z > H/2 else 0, HOLE)
            if i > 0: m.box(x-pw+3.5, x-3.5, D+6, D+8, H*0.47, H*0.51, 0xFF3A3F45)  # unión de manetas
        din_clip(m, W, H)
    elif fam == 'barrier':
        # Pepperl+Fuchs serie K: caja turquesa, bornes azules (lado Ex) arriba y grises/rojos abajo, LED al frente.
        body = TEAL
        m.box(0, W, 0, D*0.78, 0, H, body)
        prism_x(m, 0, W, [(D*0.78, H*0.2), (D, H*0.24), (D, H*0.76), (D*0.78, H*0.8)], body)
        plug_tb(m, 0.2, W-0.2, D*0.4, D*0.78+0.6, 0.4, H*0.17, 1, it.get('tb_bot', 0xFF8C9298), top=False)
        plug_tb(m, 0.2, W-0.2, D*0.4, D*0.78+0.6, H*0.83, H-0.4, 1, it.get('tb_top', PF_BLUE), top=True)
        m.box(W*0.15, W*0.85, D, D+0.3, H*0.36, H*0.66, 0xFFF4F4F4)            # etiqueta
        m.box(W*0.3, W*0.7, D, D+0.6, H*0.7, H*0.72, LED_G)
        m.box(W*0.3, W*0.7, D, D+0.6, H*0.3, H*0.32, LED_R)
        m.box(W*0.2, W*0.8, 0, 6, 0, H*0.08, 0xFF2B2F33)                      # conector Power Rail
    elif fam in ('k8ak', 'was5', '931n', 'slimmod'):
        if fam == 'k8ak':   body, front, tb = 0xFF26292D, 0xFF26292D, 0xFF3A3F45     # Omron K8AK: negro
        elif fam == 'was5': body, front, tb = 0xFFDCDDDA, WE_ORANGE, WE_BEIGE        # Weidmüller: gris con naranja
        else:               body, front, tb = RA_GREY, RA_GREY, RA_GREY             # 931N: gris claro escalonado
        m.box(0, W, 0, D*0.55, 0, H, body)
        prism_x(m, 0, W, [(D*0.55, H*0.15), (D*0.8, H*0.2), (D*0.8, H*0.8), (D*0.55, H*0.85)], body)
        m.box(0.3, W-0.3, D*0.8, D, H*0.24, H*0.76, body)
        for top in (False, True):
            z0, z1 = (H*0.84, H-0.4) if top else (0.4, H*0.16)
            plug_tb(m, 0.25, W-0.25, D*0.35, D*0.62, z0, z1, max(1, int(W//7)), tb, top)
        if fam == 'k8ak':
            for z in (H*0.6, H*0.44):                                          # dos potenciómetros
                m.cyl_y(W/2, z, 4.2, D, D+1.6, 0xFF8C9298, 18); m.box(W/2-0.5, W/2+0.5, D+1.6, D+1.9, z-3.5, z+3.5, BLACK)
            leds(m, W*0.2, H*0.7, D, 3, 0, LED_G, 2, 1.5, vertical=False)
            m.box(W*0.6, W*0.75, D, D+0.5, H*0.7, H*0.715, LED_R)
            m.box(W*0.12, W*0.88, D, D+0.3, H*0.27, H*0.36, 0xFF4A4F55)
        elif fam == 'was5':
            m.box(0.3, W-0.3, D, D+0.4, H*0.64, H*0.76, front)                 # franja naranja
            m.box(W*0.2, W*0.8, D, D+0.3, H*0.3, H*0.58, WHITE)
            m.box(W*0.4, W*0.6, D+0.4, D+0.8, H*0.68, H*0.7, LED_G)
        else:
            m.box(W*0.15, W*0.85, D, D+0.3, H*0.3, H*0.6, WHITE)
            m.box(W*0.3, W*0.7, D, D+0.5, H*0.66, H*0.68, LED_G)
    elif fam == 'chassis':
        # 1756-A10: aluminio con guías negras de las ranuras arriba y abajo.
        n = it['slots']; t = 2
        m.box(0, W, 0, 4, 0, H, ALU)
        m.box(0, t, 0, D*0.92, 0, H, ALU); m.box(W-t, W, 0, D*0.92, 0, H, ALU)
        m.box(0, W, 0, D*0.92, 0, 4, ALU); m.box(0, W, 0, D*0.92, H-4, H, ALU)
        pitch = (W-2*t)/n
        for i in range(n):
            x = t+i*pitch
            m.box(x+pitch*0.3, x+pitch*0.7, 4, 9, H*0.3, H*0.7, DARK)          # conector del backplane
            m.box(x+pitch*0.42, x+pitch*0.58, 4, D*0.9, 4, 14, TB_BLACK)        # guía inferior
            m.box(x+pitch*0.42, x+pitch*0.58, 4, D*0.9, H-14, H-4, TB_BLACK)    # guía superior
            m.box(x+pitch*0.2, x+pitch*0.8, D*0.92-0.1, D*0.92+0.2, H-3.5, H-1, WHITE)  # número de ranura
        m.box(-12, 0, 0, 3, 0, H, ALU); m.box(W, W+12, 0, 3, 0, H, ALU)
        for x in (-6, W+6):
            for z in (10, H-10): m.cyl_y(x, z, 3, 3, 4, HOLE, 12)
    elif fam == 'module1756':
        # 1756: módulo negro con puerta frontal y franja de color; CPU y ControlNet con frente gris claro.
        z0, z1 = (169-140)/2, (169+140)/2; front = it.get('front')
        prism_x(m, 0.5, W-0.5, [(0, z0), (D-6, z0), (D-6, z1-18), (D-20, z1), (0, z1)], TB_BLACK)
        if front:   # 1756-L73 / CN2R
            prism_x(m, 1.2, W-1.2, [(D-6, z0+4), (D, z0+6), (D, z1-20), (D-6, z1-18)], front)
            m.box(5, W-5, D, D+0.4, z1-36, z1-26, 0xFF101820)                  # display de 4 caracteres
            m.box(6, W-6, D-6+(-2), D-6+0.5, z1-14, z1-8, LED_G)
            leds(m, 6, z1-42, D, it.get('leds', 3), 4.5, LED_G, 4, 2)
            if it.get('key'):
                m.cyl_y(W/2, z0+40, 6, D, D+1.5, 0xFF8C9298, 18); m.box(W/2-1, W/2+1, D+1.5, D+3, z0+36, z0+44, 0xFF5A6068)
                m.box(5, W-5, D, D+0.6, z0+60, z0+75, 0xFF6A7077)               # tapa de la tarjeta SD
            else:
                for i in range(2): m.box(W/2-6, W/2+6, D, D+1, z0+20+i*25, z0+32+i*25, 0xFF5A6068)  # conectores BNC
        else:
            m.box(1.2, W-1.2, D-6, D, z0+4, z1-22, 0xFF2E3338)                 # puerta
            m.box(1.2, W-1.2, D-6, D-1, z1-22, z1-17, it.get('band', 0xFF2B9BB8)) # franja de color
            m.box(3, W-3, D, D+0.3, z0+12, z1-40, 0xFFEDEDED)                  # etiqueta interior de la puerta
            m.box(4, W-4, D-14, D-13.5, z1-17, z1-4, 0xFF101418)                # ventana de LED (en el bisel)
            nl = it.get('leds', 1)
            for i in range(min(nl, 16)):
                col = LED_G if i % 2 == 0 else 0xFF2E7D32
                m.box(5+(i % 8)*((W-10)/8), 7+(i % 8)*((W-10)/8), D-13.5, D-13, z1-14+(i//8)*4, z1-12.5+(i//8)*4, col)
            m.box(W/2-6, W/2+6, D, D+3, z0+5, z0+10, 0xFF3A4046)                # tirador
    elif fam == 'bigps':
        # 1756-PA75 / PA75R: caja negra con aletas; frente gris claro.
        m.box(0, W, 0, D-6, 0, H, TB_BLACK)
        for i in range(int(W//6)):
            x = 2+i*6; m.box(x, x+2.5, 6, D-10, H, H+5, 0xFF33383D)              # aletas superiores
        m.box(2, W-2, D-6, D, 3, H-3, it.get('front', RA_GREY))
        m.box(6, W-6, D, D+0.3, H*0.62, H*0.92, WHITE)
        m.box(W*0.15, W*0.85, D, D+4, H*0.08, H*0.3, 0xFF3A3F45)               # tapa de bornes
        m.box(W*0.15, W*0.27, D, D+3, H*0.4, H*0.52, 0xFF2B2F33)              # interruptor
        m.box(W*0.18, W*0.24, D+3, D+5, H*0.46, H*0.5, 0xFF5A6068)
        m.box(W*0.4, W*0.46, D, D+0.6, H*0.48, H*0.51, LED_G)
    elif fam in ('dynamix', 'tbase'):
        # 1444: base gris claro con bornes enchufables negros arriba y abajo; módulo gris con bisel inclinado.
        base = 35.7
        m.box(0, W, 0, base*0.8, 0, H, LGREY)
        for top in (False, True):
            z0, z1 = (H-16, H-1) if top else (1, 16)
            plug_tb(m, 2, W-2, base*0.3, base, z0, z1, max(2, int(W//6)), TB_BLACK, top)
        if fam == 'dynamix':
            prism_x(m, 2, W-2, [(base*0.8, H*0.12), (D-8, H*0.15), (D, H*0.25), (D, H*0.75), (D-8, H*0.85), (base*0.8, H*0.88)], LGREY)
            m.box(5, W-5, D, D+0.3, H*0.55, H*0.72, WHITE)
            for i in range(it.get('leds', 4)):
                m.box(6+i*((W-12)/max(1, it.get('leds', 4))), 9+i*((W-12)/max(1, it.get('leds', 4))), D, D+0.6, H*0.48, H*0.5, LED_G)
            for k in range(2 if W > 80 else 1):                                 # RJ45 Ethernet
                x = W/2-14+k*16 if W > 80 else W/2-6
                m.box(x, x+12, D-12, D+0.2, H*0.28, H*0.36, 0xFF8C9298); m.box(x+1.5, x+10.5, D, D+0.3, H*0.29, H*0.34, HOLE)
        din_clip(m, W, H)
    elif fam == 'rtcnet':
        # Woodward RTCnet/LinkNet HT: caja negra con bornes enchufables verdes en los bordes.
        m.box(0, W, 0, D, 0, H, 0xFF22262A)
        for top in (False, True):
            z0 = H-14 if top else 0
            plug_tb(m, 8, W-8, D*0.3, D+8, z0, z0+14, 10, TB_GREEN, top)
        m.box(4, W-4, D, D+0.3, H*0.5, H*0.8, WHITE)
        leds(m, 8, H*0.42, D, 4, 7, LED_G, 3, 1.6, vertical=False)
        m.box(W-22, W-8, D, D+2, H*0.38, H*0.46, TB_GREEN)                      # conector de red
        din_clip(m, W, H)
    elif fam == 'boxpc':
        # Advantech UNO-2484G: caja de aluminio plateada con aletas y puertos en la cara frontal de E/S.
        m.box(0, W, 0, D-8, 0, H, ALU)
        for i in range(int((W-6)//7)): m.box(4+i*7, 7+i*7, D-8, D, 8, H-8, ALU_D)   # aletas disipadoras
        m.box(-10, 0, 0, 2, 10, H-10, ALU_D); m.box(W, W+10, 0, 2, 10, H-10, ALU_D)
        x = 10
        for kind, w in (('rj', 14), ('rj', 14), ('rj', 14), ('rj', 14), ('usb', 7), ('usb', 7), ('hdmi', 15), ('dp', 15), ('pw', 12)):
            col = {'rj': 0xFF2B2F33, 'usb': 0xFF2F6DB5, 'hdmi': HOLE, 'dp': HOLE, 'pw': TB_GREEN}[kind]
            m.box(x, x+w, D*0.3, D*0.7, -1, 0, col); x += w + 6
        m.box(W-16, W-10, D*0.4, D*0.6, -0.8, 0, LED_G)
    elif fam == 'proximitor':
        # Bently Nevada 3300 XL: caja azul, etiqueta blanca, bornes verdes y conector del cable de extensión.
        m.box(0, W, 0, D, 0, H, BN_BLUE)
        m.box(3, W-3, D, D+0.3, H*0.25, H*0.72, WHITE)
        plug_tb(m, W*0.5, W-3, D*0.4, D+3, H-10, H, 5, TB_GREEN, True)
        m.cyl_z(W*0.22, D/2, 5, H, H+7, 0xFFC8B273, 16)                        # conector (latón)
        m.box(2, W-2, 0, 3, -3, 0, 0xFF3A3F45)                                  # pie de montaje DIN
        din_clip(m, W, H)
    elif fam == 'busbar':
        # Panduit GB2B: barra de cobre estañado sobre aisladores.
        for x in (14, W-14):
            m.cyl_y(x, H/2, 11, 0, 25, 0xFF2B2F33, 18)
        m.box(0, W, 25, 31.35, 0, H, 0xFFCDD1D4)
        for i in range(12):
            x = 25 + i*(W-50)/11
            for z in (H*0.3, H*0.7): m.cyl_y(x, z, 3, 31.3, 31.5, HOLE, 12)
    elif fam == 'doorswitch':
        # nVent Hoffman ADSW: cuerpo gris con pulsador metálico y lengüetas de conexión.
        m.box(0, W, 0, D*0.75, 0, H, 0xFF5A6068)
        m.box(2, W-2, D*0.75, D*0.8, 2, H-2, 0xFF6A7077)
        m.cyl_y(W/2, H/2, 6, D*0.8, D, 0xFFC0C4C8, 16)
        for x in (W*0.25, W*0.75): m.box(x-2.5, x+2.5, D*0.1, D*0.4, -6, 0, 0xFFC8B273)
    elif fam == 'serialsrv':
        # USR-TCP232-410S: caja metálica negra, DB9 y RJ45 en un lado, bornes verdes RS485 en el otro.
        m.box(0, W, 0, D, 0, H, 0xFF22262A)
        m.box(W*0.3, W*0.7, D*0.3, D*0.8, H, H+6, 0xFFB0B5BA)
        plug_tb(m, W*0.08, W*0.5, D*0.15, D*0.85, -6, 0, 4, TB_GREEN, False)
        m.box(W*0.6, W*0.9, D*0.2, D*0.85, -5, 0, 0xFFB0B5BA)
        m.box(5, W*0.6, D, D+0.3, H*0.6, H*0.85, 0xFF3A3F45)
        leds(m, W*0.65, H*0.75, D, 4, 4, LED_G, 2.5, 1.5)
    # ---------------- puerta: Y=0 en la cara exterior de la puerta, Y>0 sobresale, Y<0 por detrás
    elif fam in ('door_pilot', 'door_button', 'door_selector', 'door_estop'):
        # 800H: operador de plástico negro resistente a la corrosión, tuerca anular y bloque de contactos 800T-XA.
        cx, cz = W/2, H/2
        m.box(cx-14, cx+14, -D, -D+38, cz-14, cz+14, 0xFF5A6068)               # bloque de contactos
        m.box(cx-11, cx+11, -D-1, -D, cz-8, cz+8, 0xFFB0B5BA)                  # tornillos de conexión
        m.box(cx-17, cx+17, -D+38, -6, cz-17, cz+17, 0xFF2B2F33)               # adaptador
        m.cyl_y(cx, cz, 20, -6, -2, 0xFF2B2F33, 6)                             # tuerca de fijación (hexagonal)
        m.cyl_y(cx, cz, 30.5/2, -2, 0, 0xFF2B2F33, 24)
        if fam == 'door_estop':
            m.cyl_y(cx, cz, W/2, 0, 1, YEL, 32)                                 # placa de leyenda amarilla
            m.cyl_y(cx, cz, 17, 1, 14, BLACK, 24)
            m.cyl_y(cx, cz, 20, 14, 20, 0xFFC62828, 28)
            m.cyl_y(cx, cz, 22.5, 20, 30, LED_R, 32)                            # seta
        else:
            m.cyl_y(cx, cz, W/2, 0, 4, 0xFF2B2F33, 28)                          # anillo frontal negro
            m.cyl_y(cx, cz, W/2-2.5, 4, 6, 0xFF3A3F45, 28)
            col = it.get('color', BLACK)
            if fam == 'door_pilot':
                m.cyl_y(cx, cz, 14, 6, 12, col, 24); m.cyl_y(cx, cz, 10, 12, 16, col, 20)   # lente en cúpula
            elif fam == 'door_button':
                m.cyl_y(cx, cz, 15, 6, 8, 0xFF2B2F33, 24)                       # protector
                m.cyl_y(cx, cz, 12.5, 6, 10 if not it.get('lit') else 13, col, 24)
            else:
                m.cyl_y(cx, cz, 15, 6, 9, BLACK, 24)
                m.box(cx-4, cx+4, 9, 24, cz-14, cz+14, 0xFF2B2F33)              # maneta
                m.box(cx-1, cx+1, 24, 24.3, cz+8, cz+13, WHITE)                 # indicador de posición
    elif fam in ('hmi_ctrl', 'monitor', 'meter'):
        style = it.get('style', fam)
        bez = {'meter': 4, 'monitor': 8}.get(fam, 6)
        bez_col = {'505': 0xFF2E3338, 'easygen': 0xFFD3D7DB, 'flex': 0xFF22262A, 'monitor': ALU, 'meter': PD_BLUE}[style]
        m.box(0, W, 0, bez, 0, H, bez_col)                                       # marco frontal
        m.box(W*0.06, W*0.94, -D+bez, 0, H*0.06, H*0.94, 0xFF5A6068 if fam != 'meter' else 0xFF2B2F33)  # cuerpo detrás
        if fam == 'monitor':
            m.box(W*0.04, W*0.96, bez, bez+0.4, H*0.05, H*0.92, 0xFF111418)
            for i in range(5): m.box(W*0.75+i*14, W*0.75+i*14+8, bez, bez+1, H*0.015, H*0.035, KEY)
            m.box(W*0.7, W*0.7+4, bez, bez+0.5, H*0.02, H*0.03, LED_G)
        elif fam == 'meter':
            m.box(W*0.07, W*0.74, bez, bez+0.5, H*0.18, H*0.82, LCD)
            for i in range(5): m.box(W*0.1+i*W*0.12, W*0.1+i*W*0.12+W*0.09, bez+0.5, bez+0.6, H*0.4, H*0.75, LCD_D)
            for i in range(4): m.box(W*0.8, W*0.92, bez, bez+2, H*(0.12+i*0.2), H*(0.12+i*0.2)+H*0.13, 0xFF22262A)
        elif style == '505':
            # 505: pantalla arriba a la izquierda, teclado numérico a la derecha, teclas de función y paro rojo.
            m.box(W*0.06, W*0.6, bez, bez+0.5, H*0.5, H*0.9, LCD)
            for i in range(4): m.box(W*0.08+i*W*0.13, W*0.08+i*W*0.13+W*0.1, bez, bez+2.5, H*0.4, H*0.46, KEY_L)   # softkeys
            for r in range(4):
                for c in range(3):
                    x = W*0.66 + c*W*0.09; z = H*0.38 + r*H*0.13
                    m.box(x, x+W*0.07, bez, bez+3, z, z+H*0.09, KEY)
            for i, col in enumerate((0xFF2E9E4D, 0xFF2E9E4D, KEY, KEY, 0xFFE8C21F)):
                x = W*0.08 + i*W*0.1; m.box(x, x+W*0.08, bez, bez+3, H*0.2, H*0.3, col)
            m.box(W*0.66, W*0.92, bez, bez+4, H*0.1, H*0.3, LED_R)              # tecla de disparo (trip)
        elif style == 'easygen':
            # easYgen-3200: frente gris claro, pantalla azul y softkeys a los dos lados.
            m.box(W*0.2, W*0.8, bez, bez+0.5, H*0.3, H*0.88, 0xFF2F5C9A)
            for side in (W*0.08, W*0.84):
                for r in range(4): m.box(side, side+W*0.08, bez, bez+3, H*0.36+r*H*0.13, H*0.36+r*H*0.13+H*0.09, KEY)
            for i in range(6):
                x = W*0.12 + i*W*0.13; m.box(x, x+W*0.1, bez, bez+3, H*0.08, H*0.2, KEY if i not in (0, 5) else (0xFF2E9E4D if i == 0 else LED_R))
            for i in range(3): m.box(W*0.86+i*W*0.03, W*0.88+i*W*0.03, bez, bez+0.6, H*0.9, H*0.92, LED_G)
        else:
            # Flex500 / Vertex: pantalla de 8" con teclas bajo la pantalla.
            m.box(W*0.08, W*0.92, bez, bez+0.5, H*0.3, H*0.92, 0xFF111418)
            for i in range(8):
                x = W*0.08 + i*W*0.106; m.box(x, x+W*0.09, bez, bez+2.5, H*0.15, H*0.24, KEY)
            m.box(W*0.85, W*0.92, bez, bez+0.6, H*0.06, H*0.1, LED_G)
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
