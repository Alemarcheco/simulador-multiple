"""Genera plano-ejemplo.pdf: un plano eléctrico de prueba como los que exportan los programas de CAD eléctrico
(portada, esquema con designaciones, lista de materiales, plano de bornes y lista de cables).
Uso: python3 make_plano.py  (necesita reportlab)."""
from reportlab.lib.pagesizes import A3, landscape
from reportlab.pdfgen import canvas
import os

W, H = landscape(A3)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "plano-ejemplo.pdf")
c = canvas.Canvas(out, pagesize=(W, H))

def cajetin(n, titulo):
    c.setFont("Helvetica", 8)
    c.rect(20, 20, W - 40, H - 40)
    c.rect(W - 420, 20, 400, 60)
    c.drawString(W - 410, 62, "Proyecto: Bomba de riego 5,5 kW   Formato A3")
    c.drawString(W - 410, 46, titulo)
    c.drawString(W - 410, 30, f"Hoja {n} / 5   Rev. 2   Fecha 12.03.2025")

def tabla(y, filas, xs, size=9):
    c.setFont("Helvetica", size)
    for fila in filas:
        for x, txt in zip(xs, fila):
            c.drawString(x, y, txt)
        y -= 16
    return y

# 1 portada
cajetin(1, "Portada")
c.setFont("Helvetica-Bold", 28); c.drawString(80, H - 140, "Cuadro de mando bomba de riego")
c.setFont("Helvetica", 12); c.drawString(80, H - 170, "Arranque directo con guardamotor, marcha/paro y pilotos")
c.showPage()

# 2 esquema (texto disperso como en un dibujo)
cajetin(2, "Esquema de potencia y mando")
c.setFont("Helvetica", 9)
for x, y, t in [(80, 700, "-Q1"), (80, 688, "C16 3P"), (200, 700, "-QM1"), (200, 688, "9-14A"), (320, 700, "-KM1"),
                (320, 600, "-M1"), (320, 588, "5,5kW"), (500, 700, "-F2"), (500, 688, "2A"), (560, 700, "-S0"),
                (620, 700, "-S1"), (680, 700, "-KM1"), (740, 700, "-H1"), (800, 700, "-H2"), (860, 700, "-B1"),
                (80, 500, "1"), (95, 500, "2"), (330, 500, "A1"), (345, 500, "A2"), (40, 750, "2.3"), (60, 750, "A4")]:
    c.drawString(x, y, t)
c.showPage()

# 3 lista de materiales
cajetin(3, "Lista de materiales")
c.setFont("Helvetica-Bold", 11); c.drawString(60, H - 80, "Lista de materiales")
tabla(H - 110, [
    ["Designación", "Cant.", "Descripción", "Referencia"],
    ["-Q1", "1", "Interruptor automático magnetotérmico 3P C16", "MT-3P-C16"],
    ["-QM1", "1", "Guardamotor 9-14 A", "GM-1014"],
    ["-KM1", "1", "Contactor 3P 25 A bobina 230 V", "CT25-230"],
    ["-F2", "1", "Interruptor automático 1P C2 mando", "MT-1P-C2"],
    ["-S0", "1", "Pulsador de seta de emergencia", "PS-EM40"],
    ["-S1", "1", "Pulsador marcha verde", "PB22-VE"],
    ["-H1", "1", "Piloto verde en marcha 230 V", "PL22-VE"],
    ["-H2", "1", "Piloto rojo disparo 230 V", "PL22-RO"],
    ["-M1", "1", "Motor bomba 5,5 kW 400 V", "MOT-55"],
    ["-B1", "1", "Presostato de mínima", "PR-10B"],
    ["-X1", "1", "Regleta de bornes 2,5 mm²", "RB-25"],
], [60, 160, 210, 560])
c.showPage()

# 4 plano de bornes
cajetin(4, "Plano de bornes")
c.setFont("Helvetica-Bold", 11); c.drawString(60, H - 80, "Plano de bornes")
c.setFont("Helvetica-Bold", 10); c.drawString(60, H - 110, "-X1")
tabla(H - 130, [
    ["1", "-KM1:2", "-M1:U1"],
    ["2", "-KM1:4", "-M1:V1"],
    ["3", "-KM1:6", "-M1:W1"],
    ["4", "-PE:PE", "-M1:PE"],
    ["5", "-S1:14", "-B1:1"],
    ["6", "-KM1:A1", "-B1:2"],
    ["7", "-Q1:1", "L1"],
    ["8", "-Q1:3", "L2"],
    ["9", "-Q1:5", "L3"],
    ["10", "-H2:X2", "N"],
], [60, 120, 260])
c.showPage()

# 5 lista de cables
cajetin(5, "Lista de cables")
c.setFont("Helvetica-Bold", 11); c.drawString(60, H - 80, "Lista de cables")
tabla(H - 110, [
    ["Hilo", "Origen", "Borne", "Destino", "Borne", "Sección", "Color"],
    ["L1", "-Q1", "2", "-QM1", "1", "2,5 mm²", "negro"],
    ["L2", "-Q1", "4", "-QM1", "3", "2,5 mm²", "negro"],
    ["L3", "-Q1", "6", "-QM1", "5", "2,5 mm²", "negro"],
    ["11", "-QM1", "2", "-KM1", "1", "2,5 mm²", "negro"],
    ["12", "-QM1", "4", "-KM1", "3", "2,5 mm²", "negro"],
    ["13", "-QM1", "6", "-KM1", "5", "2,5 mm²", "negro"],
    ["21", "-Q1", "2", "-F2", "1", "1 mm²", "rojo"],
    ["22", "-F2", "2", "-S0", "11", "1 mm²", "rojo"],
    ["23", "-S0", "12", "-S1", "13", "1 mm²", "rojo"],
    ["24", "-S1", "14", "-KM1", "13", "1 mm²", "rojo"],
    ["30", "-QM1", "13", "-Q1", "2", "1 mm²", "rojo"],
    ["25", "-KM1", "14", "-H1", "X1", "1 mm²", "rojo"],
    ["N", "-KM1", "A2", "-H1", "X2", "1 mm²", "azul"],
    ["N", "-H1", "X2", "-H2", "X2", "1 mm²", "azul"],
    ["26", "-QM1", "14", "-H2", "X1", "1 mm²", "rojo"],
], [60, 120, 200, 260, 340, 400, 480])
c.showPage()
c.save()
print(out)
