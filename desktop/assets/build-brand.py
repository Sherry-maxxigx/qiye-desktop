"""Render the Qiye vector mark to Windows icons. Requires Pillow and NumPy at build time."""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SIZE = 1536
SCALE = SIZE / 512
LEFT = [('M', 256, 315), ('C', 205, 240, 146, 207, 94, 204),
        ('C', 100, 269, 173, 329, 256, 352), ('Z',)]
RIGHT = [('M', 256, 315), ('C', 295, 234, 364, 188, 425, 174),
         ('C', 414, 261, 344, 331, 256, 352), ('Z',)]
FOLD_LEFT = [('M', 132, 233), ('C', 176, 246, 218, 280, 244, 315)]
FOLD_RIGHT = [('M', 383, 215), ('C', 338, 238, 297, 277, 268, 315)]

def path_text(commands):
    return ' '.join(' '.join(map(str, command)) for command in commands)

def points(commands):
    output, current = [], (0, 0)
    for command in commands:
        if command[0] == 'M':
            current = command[1:]
            output.append(current)
        elif command[0] == 'C':
            start = current
            c1, c2, end = command[1:3], command[3:5], command[5:7]
            for t in np.linspace(0, 1, 90)[1:]:
                v = 1 - t
                output.append(tuple(v**3 * start[i] + 3*v*v*t*c1[i] + 3*v*t*t*c2[i] + t**3*end[i] for i in (0, 1)))
            current = end
    return [(round(x*SCALE), round(y*SCALE)) for x, y in output]

def gradient(first, last, horizontal=0.35):
    y, x = np.ogrid[0:SIZE, 0:SIZE]
    t = (x * horizontal + y * (1-horizontal)) / (SIZE-1)
    a = np.array(tuple(bytes.fromhex(first)), dtype=float)
    b = np.array(tuple(bytes.fromhex(last)), dtype=float)
    rgb = (a + (b-a) * t[..., None]).clip(0, 255).astype('uint8')
    return Image.fromarray(rgb, 'RGB').convert('RGBA')

def fill(canvas, commands, first, last):
    mask = Image.new('L', (SIZE, SIZE))
    ImageDraw.Draw(mask).polygon(points(commands), fill=255)
    canvas.alpha_composite(Image.composite(gradient(first, last), Image.new('RGBA', (SIZE, SIZE)), mask))

image = Image.new('RGBA', (SIZE, SIZE))
mask = Image.new('L', (SIZE, SIZE))
ImageDraw.Draw(mask).rounded_rectangle((16*SCALE, 16*SCALE, 496*SCALE, 496*SCALE), radius=116*SCALE, fill=255)
image = Image.composite(gradient('62548F', '252D50', 0.4), image, mask)

# A quiet dusk sun above the open pages, without small decorative details.
sun = Image.new('RGBA', image.size)
ImageDraw.Draw(sun).ellipse((334*SCALE, 110*SCALE, 390*SCALE, 166*SCALE), fill='#F3C77F')
image.alpha_composite(sun)
fill(image, LEFT, 'FFFAEE', 'DDD9ED')
fill(image, RIGHT, 'E3FAE9', '73BEB7')
folds = Image.new('RGBA', image.size)
draw = ImageDraw.Draw(folds)
draw.line(points(FOLD_LEFT), fill=(255,255,255,145), width=round(4*SCALE), joint='curve')
draw.line(points(FOLD_RIGHT), fill=(245,255,248,140), width=round(4*SCALE), joint='curve')
image.alpha_composite(folds)
ROOT.mkdir(exist_ok=True)
icon = image.resize((512,512), Image.Resampling.LANCZOS)
icon.save(ROOT/'qiye-icon.png')
icon.resize((128,128),Image.Resampling.LANCZOS).save(ROOT/'qiye-preview.png')
for destination in (ROOT.parent/'app.ico',ROOT/'qiye.ico',ROOT/'qiye-taskbar.ico'):
    icon.save(destination,format='ICO',bitmap_format='bmp',sizes=[(s,s) for s in (16,20,24,32,40,48,64,96,128,256)])

svg = '''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="栖页：书页与飞鸟">
<defs>
 <linearGradient id="bg" x2=".6" y2="1"><stop stop-color="#62548F"/><stop offset="1" stop-color="#252D50"/></linearGradient>
 <linearGradient id="paper" x2=".65" y2="1"><stop stop-color="#FFFAEE"/><stop offset="1" stop-color="#DDD9ED"/></linearGradient>
 <linearGradient id="mint" x2=".65" y2="1"><stop stop-color="#E3FAE9"/><stop offset="1" stop-color="#73BEB7"/></linearGradient>
</defs>
<rect x="16" y="16" width="480" height="480" rx="116" fill="url(#bg)"/>
<circle cx="362" cy="138" r="28" fill="#F3C77F"/>
<path d="LEFT" fill="url(#paper)"/>
<path d="RIGHT" fill="url(#mint)"/>
<g fill="none" stroke="#FFFFFF" stroke-opacity=".55" stroke-width="4" stroke-linecap="round">
 <path d="FOLD_LEFT"/><path d="FOLD_RIGHT"/>
</g>
</svg>'''
for name, commands in [('FOLD_LEFT',FOLD_LEFT),('FOLD_RIGHT',FOLD_RIGHT),('LEFT',LEFT),('RIGHT',RIGHT)]:
    svg=svg.replace(name,path_text(commands))
(ROOT/'qiye.svg').write_text(svg,encoding='utf-8')
print('Created vector source, 512 px PNG, preview and 10-size Windows ICO.')
