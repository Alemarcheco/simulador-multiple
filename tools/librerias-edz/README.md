# Paquetes .edz de la librería de Tableros

`gen.py` genera los paquetes de `src/Simulador.Tableros/Librerias/` a partir de `data.py`
(componentes de tablero del inventario de materiales, con la ficha de donde sale cada medida).

```
pip install py7zr
python3 gen.py ../../src/Simulador.Tableros/Librerias
```

Cada .edz es un archivo 7z con la misma estructura que lee el importador:

- `items/<marca>_<referencia>.xml`: pieza con propiedades `P_ARTICLE_*` de EPLAN (referencia, marca,
  descripción, ancho, alto, fondo, potencia disipada) y un elemento `<simulador>` con el tipo de aparato,
  dónde se monta, si va en carril, si las medidas están comprobadas y la fuente.
- `3D/<nombre>.obj` + `.mtl`: modelo 3D en mm (X ancho, Y hacia fuera desde la superficie de montaje,
  Z alto; en la puerta, Y = 0 es la cara exterior).
- `3D/<nombre>.stp`: el mismo modelo en STEP AP214 (B-rep facetado) para otros programas de CAD.

No son paquetes de EPLAN Data Portal: EPLAN no los importa como macros (sus macros .ema son propietarias).
