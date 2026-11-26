# Componentes de tablero de la pestaña MATERIALES (n.º de fila del Excel en "rows").
# Medidas en mm: W ancho, H alto, D fondo (desde la placa/carril; en puerta, fondo detrás de la puerta).
RA = "Rockwell Automation"
def it(brand, ref, kind, desc, W, H, D, heat, loc, rail, ver, src, fam, rows, keys=None, **kw):
    return dict(brand=brand, ref=ref, kind=kind, desc=desc, W=W, H=H, D=D, heat=heat, loc=loc, rail=rail,
                verified=ver, source=src, family=fam, rows=rows, keys=keys or [], **kw)

S_1756_TD006 = "Rockwell 1756-TD006 (chasis) y plano 1756-A10 (configurator.rockwellautomation.com)"
S_1756_TD005 = "Rockwell 1756-TD005J (fuentes ControlLogix), tabla Dimensions HxWxD"
S_1444 = "Rockwell 1444-TD001E (Dynamix 1444), tabla Dimensions HxWxD"
S_700 = "Ficha 700-HLT1U1 de NHP (distribuidor oficial Rockwell): 6,2 × 88 × 76 mm"
S_ABB = "ABB, ficha S200 (2CDC002180D0203) y ficha S201 (Farnell): 17,5 mm/polo × 88 × 69 mm"
S_PF1 = "Pepperl+Fuchs, ficha 70150440 (KCD2-SCD-Ex1): 12,5 × 124 × 114 mm"
S_PF2 = "Pepperl+Fuchs, ficha 189784 (KFD2-EB2.R4A.B): 20 × 119 × 115 mm"
S_OM = "Omron, catálogo N188-E1-04 (K8AK-LS): 22,5 × 100 × 90 mm"
S_WE = "Weidmüller, ficha 8581160000 (WAS5 CCC 2OLP): 17,5 × 112,4 × 92,4 mm"
S_WW = "Woodward"
items = [
 # ---------------- Allen-Bradley relés de interfaz 700-HL
 it("Allen-Bradley","700-HLT1U1","rele","Relé de interfaz 700-HL, 1 conmutado, bobina 110/125 V AC/DC",6.2,88,76,0.5,"Plate",True,True,S_700,"slimrelay",[49]),
 it("Allen-Bradley","700-HLT1Z24","rele","Relé de interfaz 700-HL, 1 conmutado, bobina 24 V AC/DC",6.2,88,76,0.4,"Plate",True,True,S_700+" (misma caja que 700-HLT1U1)","slimrelay",[50],["700-HLT1U"]),
 it("Allen-Bradley","700-HLT12Z24","rele","Relé de interfaz 700-HL, 2 conmutados, bobina 24 V",12.4,88,76,0.6,"Plate",True,False,"Medida típica del 700-HL de 2 contactos (doble ancho): comprobar en 700-TD552","slimrelay",[48],["700-HLT12"]),
 # ---------------- ControlLogix 1756
 it("Allen-Bradley","1756-A10","plc","Chasis ControlLogix de 10 ranuras",397.1,169,144.5,0,"Plate",False,True,S_1756_TD006,"chassis",[197,198,199,200],slots=10),
 it("Allen-Bradley","1756-PA75R","fuente","Fuente redundante ControlLogix 85–265 V AC",145,175,137,25,"Plate",False,True,S_1756_TD005,"bigps",[197],front=0xFF9AA2AA),
 it("Allen-Bradley","1756-PA75","fuente","Fuente ControlLogix 85–265 V AC, 75 W",112,140,145,25,"Plate",False,True,S_1756_TD005,"bigps",[200]),
 it("Allen-Bradley","1756-L73","plc","Controlador ControlLogix 5573 (8 MB)",35,140,144.5,5,"Plate",False,False,"Módulo de 1 ranura: alto y fondo del plano 1756-A10; ancho de ranura típico 35 mm","module1756",[199],["LOGIX5573"],leds=4,display=True,front=0xFFC9CDD1,key=True),
 it("Allen-Bradley","1756-CN2R","plc","Módulo ControlNet redundante",35,140,144.5,5,"Plate",False,False,"Módulo de 1 ranura: alto y fondo del plano 1756-A10; ancho de ranura típico 35 mm","module1756",[198],["1756-CN2"],leds=3,display=True,front=0xFF9AA2AA),
 it("Allen-Bradley","1756-IF16","plc","Módulo 16 entradas analógicas",35,140,144.5,3,"Plate",False,False,"Módulo de 1 ranura (1756-TD002: «Slot width 1»); alto y fondo del plano 1756-A10","module1756",[200,207],leds=1),
 it("Allen-Bradley","1756-IB16","plc","Módulo 16 entradas digitales 24 V DC",35,140,144.5,3,"Plate",False,False,"Módulo de 1 ranura (1756-TD002: «Slot width 1»); alto y fondo del plano 1756-A10","module1756",[200],leds=16),
 it("Allen-Bradley","1756-IB32","plc","Módulo 32 entradas digitales 24 V DC",35,140,144.5,4,"Plate",False,False,"Módulo de 1 ranura (1756-TD002: «Slot width 1»); alto y fondo del plano 1756-A10","module1756",[205],leds=32),
 it("Allen-Bradley","1756-OB16E","plc","Módulo 16 salidas digitales 24 V DC con protección",35,140,144.5,4,"Plate",False,False,"Módulo de 1 ranura; alto y fondo del plano 1756-A10","module1756",[200,219],leds=16),
 it("Allen-Bradley","1756-OW16I","plc","Módulo 16 salidas de relé aisladas",35,140,144.5,4,"Plate",False,False,"Módulo de 1 ranura; alto y fondo del plano 1756-A10","module1756",[200],leds=16),
 it("Allen-Bradley","1756-OF8","plc","Módulo 8 salidas analógicas",35,140,144.5,4,"Plate",False,False,"Módulo de 1 ranura; alto y fondo del plano 1756-A10","module1756",[206],leds=1),
 # ---------------- Dynamix 1444
 it("Allen-Bradley","1444-DYN04-01RA","plc","Dynamix 1444, módulo de medida dinámica (vibración) de 4 canales, con base 1444-TB-A",103.5,157.9,126.4,6,"Plate",True,True,S_1444,"dynamix",[181],leds=6),
 it("Allen-Bradley","1444-RELX00-04RB","rele","Dynamix 1444, expansión de 4 relés, con base 1444-TB-B",54.7,157.9,100.4,2,"Plate",True,True,S_1444,"dynamix",[182],leds=4),
 it("Allen-Bradley","1444-TB-B","aparato","Base de bornes Dynamix 1444-TB-B (sola)",54.7,157.9,35.7,0,"Plate",True,True,S_1444,"tbase",[183]),
 # ---------------- Señalización y mando 800H (Ø30,5 mm)
 it("Allen-Bradley","800H-QR10G","piloto","Piloto 800H Ø30,5 mm verde (también ámbar/rojo cambiando la lente)",41,41,70,0.5,"Door",False,False,"Taladro Ø30,5 mm (ficha 800H-QRB10G); frente y fondo típicos del 800H: comprobar en 800-TD009","door_pilot",[88,89,195],["800H-QR24G"],color=0xFF43A047),
 it("Allen-Bradley","800H-AR2","pulsador","Pulsador 800H Ø30,5 mm rasante (verde/negro/rojo)",41,41,80,0,"Door",False,False,"Taladro Ø30,5 mm (ficha 800H); frente y fondo típicos del 800H con un bloque 800T-XA","door_button",[90,91,92,95,96,187,188,189],["800H-N5A","800H-N5B","800T-XA"],color=0xFF2E9E4D),
 it("Allen-Bradley","800H-QRB10R","pulsador","Pulsador luminoso 800H Ø30,5 mm rojo",41,41,85,0.5,"Door",False,False,"Taladro Ø30,5 mm (ficha 800H-QRB10G); frente y fondo típicos","door_button",[93],color=0xFFE53935,lit=True),
 it("Allen-Bradley","800H-HR4A","selector","Selector 800H Ø30,5 mm (2 o 3 posiciones)",41,41,80,0,"Door",False,False,"Taladro Ø30,5 mm (ficha 800H); frente y fondo típicos","door_selector",[107,108,109,191,196]),
 it("Allen-Bradley","800H-FRXT6A","seta","Pulsador de parada de emergencia 800H, seta Ø45 mm",45,45,90,0,"Door",False,False,"Seta estándar de 45 mm (800-TD009); taladro Ø30,5 mm; fondo típico","door_estop",[94]),
 it("Allen-Bradley","931N-C122","aparato","Divisor de señal analógica 931N (1 entrada, 2 salidas)",6.1,112.5,114.3,0.8,"Plate",True,False,"Medida de un listado de recambios (no oficial): comprobar","931n",[208,209,210,211,212,213,214,215],["931N-C122A","931N-C122 A"]),
 # ---------------- ABB S200
 it("ABB","S201-C6","magnetotermico","Interruptor automático S200 1P C6",17.5,88,69,1.5,"Plate",True,True,S_ABB,"mcb",[53],["2CDS251001R0064"],poles=1),
 it("ABB","S202-C6","magnetotermico","Interruptor automático S200 2P C6",35,88,69,3,"Plate",True,True,S_ABB,"mcb",[54],["2CDS252001R0064"],poles=2),
 it("ABB","S202-C10","magnetotermico","Interruptor automático S200 2P C10",35,88,69,3,"Plate",True,True,S_ABB,"mcb",[55],["2CDS252001R0104"],poles=2),
 it("ABB","S202-C32","magnetotermico","Interruptor automático S200 2P C32",35,88,69,5,"Plate",True,True,S_ABB,"mcb",[58],["2CDS252001R0324"],poles=2),
 it("ABB","S202-C40","magnetotermico","Interruptor automático S200 2P C40",35,88,69,6,"Plate",True,True,S_ABB,"mcb",[56],["2CDS252001R0404"],poles=2),
 it("ABB","S202-C63","magnetotermico","Interruptor automático S200 2P C63",35,88,69,8,"Plate",True,True,S_ABB,"mcb",[57],["2CDS252001R0634"],poles=2),
 # ---------------- Pepperl+Fuchs
 it("Pepperl+Fuchs","KCD2-SCD-Ex1","aparato","Barrera de seguridad intrínseca, salida 4–20 mA (70150440)",12.5,124,114,0.6,"Plate",True,True,S_PF1,"barrier",[203,204],["70150440"],tb_top=0xFF2F6DB5,tb_bot=0xFF8C9298),
 it("Pepperl+Fuchs","KFD2-EB2.R4A.B","fuente","Módulo de alimentación redundante Power Rail (189784)",20,119,115,2.4,"Plate",True,True,S_PF2,"barrier",[202],["189784"],tb_top=0xFF8C9298,tb_bot=0xFFC62828),
 # ---------------- Omron / Weidmüller
 it("Omron","K8AK-LS1","aparato","Controlador de nivel conductivo K8AK-LS1",22.5,100,90,1.1,"Plate",True,True,S_OM,"k8ak",[70,71],["K8AK-LS1 24VAC/DC"]),
 it("Weidmüller","WAS5 CCC 2OLP","aparato","Duplicador de señal analógica 4–20 mA, alimentado por lazo (8581160000)",17.5,112.4,92.4,0.8,"Plate",True,True,S_WE,"was5",[119],["8581160000","WAS5"]),
 # ---------------- Woodward
 it("Woodward","505D","controlador","Control digital 505 para turbinas de vapor (montaje en panel)",356,279,102,25,"Door",False,True,"Woodward, especificación 03348 (356×279×102 mm)","hmi_ctrl",[19],["8200-1300"],style="505"),
 it("Woodward","easYgen-3200","controlador","Control de grupo electrógeno easYgen-3000, montaje frontal en puerta (8440-2050)",282,217,99,15,"Door",False,True,"Woodward, especificación 37258 (frente 282×217×99 mm, hueco 249×183 mm)","hmi_ctrl",[80],["8440-2050","EASY GEN 3000","easYgen-3000"],style="easygen"),
 it("Woodward","RTCnet AIO","plc","Nodo de E/S distribuidas RTCnet, 8 EA / 2 SA 4–20 mA (8200-1102)",112,169,30,4,"Plate",True,True,"Woodward, especificación 03402 (112×169×30 mm sobre el carril, sin conectores)","rtcnet",[85],["8200-1102"]),
 it("Woodward","RTCnet DI","plc","Nodo de E/S distribuidas RTCnet, 16 ED (8200-1104)",112,169,30,3,"Plate",True,True,"Woodward, especificación 03402 (112×169×30 mm sobre el carril, sin conectores)","rtcnet",[86],["8200-1104"]),
 it("Woodward","LinkNet HT DO","plc","Nodo de E/S distribuidas LinkNet HT, 16 SD (8200-1205)",112,169,30,3,"Plate",True,False,"Misma caja que RTCnet (especificación 03402); comprobar en la ficha de LinkNet HT","rtcnet",[87],["8200-1205","LINKNETHT DO"]),
 it("Woodward","Flex500","controlador","Control Flex500 con pantalla de 8\" para montaje en panel (8200-2355)",300,250,110,20,"Door",False,False,"La especificación 03433 solo da el dibujo: medidas aproximadas, comprobar antes de taladrar","hmi_ctrl",[20],["8200-2355","82002355","FLEX 500"],style="flex"),
 it("Woodward","Vertex","controlador","Control de compresores Vertex (8200-2375)",300,250,110,20,"Door",False,False,"La especificación 03437 solo da el dibujo: medidas aproximadas, comprobar antes de taladrar","hmi_ctrl",[18],["8200-2375","VERTEX"],style="flex"),
 # ---------------- Advantech
 it("Advantech","FPM-3191G-R3BE","controlador","Monitor industrial 19\" táctil, montaje en panel (hueco 441×376,4 mm)",482,399.2,67,30,"Door",False,True,"Advantech, ficha FPM-3191G (482×399,2×67 mm, hueco 441×376,4 mm)","monitor",[21,22],["FPM-3191G"]),
 it("Advantech","UNO-2484G","plc","PC industrial sin ventilador UNO-2484G (montaje mural o carril DIN)",200,140,70,25,"Plate",False,True,"Advantech, ficha UNO-2484G (200×140×70 mm con 2 pisos)","boxpc",[23,24],["UNO-2484G-6731AE"]),
 # ---------------- Otros
 it("Bently Nevada","330180-51-05","aparato","Proximitor 3300 XL 8 mm, 5 m, montaje en carril DIN",64,89.4,32,0.5,"Plate",True,False,"Ficha 141194 (opciones de montaje DIN/panel); solo la cota de 89,4 mm se pudo leer, el resto es típico","proximitor",[64,65,66],["330180-91-05","330180-90-058","3300 XL"]),
 it("Panduit","GB2B0306TPI-1","barra_pe","Barra de tierra de cobre 1/4\" × 2\" × 12\" (BICSI)",305,51,32,0,"Plate",False,False,"Medida del nombre comercial (12\"×2\"×1/4\", distribuidor); comprobar en panduit.com","busbar",[76]),
 it("nVent Hoffman","ADSW01","aparato","Interruptor de puerta para cajas murales, 3 A / 240 V",31,30,62,0,"Plate",False,True,"nVent Hoffman, ficha ADSW (30×31×62 mm)","doorswitch",[234]),
 it("Precision Digital","PD6606-LNN","instrumento","Indicador de lazo Loop Leader 1/8 DIN (hueco 92×45 mm)",119,62,96,0.5,"Door",False,True,"Precision Digital, dibujo serie Loop Leader (119×62×96 mm, hueco 92×45 mm)","meter",[135,136],["PD6606"]),
 it("USR IOT","USR-TCP232-410S","aparato","Servidor serie RS232/RS485 a Ethernet",90,84,25,2,"Plate",False,True,"USR IOT, ficha USR-TCP232-410S (90×84×25 mm con caja y bornes)","serialsrv",[256]),
]
