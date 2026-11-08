"""Build the FlowRate Server identity kit without changing application resources."""
from pathlib import Path
import xml.etree.ElementTree as ET
import cairo
import gi
from PIL import Image

gi.require_version('Rsvg', '2.0')
from gi.repository import Rsvg

ROOT = Path(__file__).resolve().parent
SVG = ROOT / 'svg'
PNG = ROOT / 'png'
PURPLE = '#6C63FF'
DARK = '#232323'

HEADPHONES = '<path d="M112 281V240C112 160 176 100 256 100S400 160 400 240V281" fill="none" stroke="currentColor" stroke-width="30" stroke-linecap="round"/>\n<g fill="currentColor"><rect x="88" y="249" width="58" height="137" rx="27"/><rect x="366" y="249" width="58" height="137" rx="27"/></g>\n<g fill="currentColor" transform="translate(83.1 139.2) scale(.66)"><path d="M127 369V143H239V183H171V235H227V275H171V369Z"/><path fill-rule="evenodd" d="M260 369V143H313C364 143 391 167 391 208C391 238 377 258 353 269L397 369H347L310 281H304V369ZM304 183V242H313C336 242 347 231 347 212C347 193 336 183 313 183Z"/></g>'
OUTLINE = 'M108 398Q76 398 76 366V140Q76 125 88 115L132 77Q154 58 178 58H400Q436 58 436 94V316Q436 332 424 342L380 380Q359 398 334 398Z'
SIDE = 'M334 116H344Q359 116 372 105L427 67Q436 78 436 94V316Q436 332 424 342L380 380Q359 398 334 398Z'
FRONT = 'M88 115Q98 116 108 116H334Q368 116 368 150V366Q368 398 334 398H108Q76 398 76 366V140Q76 125 88 115Z'


def cube(app=False):
    front, top, side, logo = ('#FFFFFF', '#E2DFFF', '#BDB8FF', PURPLE) if app else (PURPLE, '#918AFF', '#4F46D4', '#FFFFFF')
    return (f'<g transform="translate(0 28)"><path d="{OUTLINE}" fill="{top}"/>'
            f'<path d="{SIDE}" fill="{side}"/><path d="{FRONT}" fill="{front}"/>'
            f'<g transform="translate(63.28 101) scale(0.62)">{HEADPHONES.replace("currentColor", logo)}</g></g>')


def monochrome(color):
    return (f'<g transform="translate(0 28)"><path d="{OUTLINE}" fill="none" stroke="{color}" stroke-width="13" stroke-linejoin="round"/>'
            f'<path d="M88 116H334Q368 116 368 150V390M358 126L427 67" fill="none" stroke="{color}" stroke-width="9" stroke-linejoin="round" stroke-linecap="round"/>'
            f'<g transform="translate(63.28 101) scale(0.62)">{HEADPHONES.replace("currentColor", color)}</g></g>')


def tray(color):
    # Compact cube, heavier FR letters, and headphones for native tray sizes.
    return (f'<g stroke="{color}" stroke-linejoin="round" stroke-linecap="round" fill="none">'
            '<path d="M6.5 28Q4 28 4 25.5V10Q4 8.7 5 7.9L9 4.5Q10 3.5 11.5 3.5H25.5Q28 3.5 28 6V21.5Q28 22.7 27 23.5L23 27Q21.8 28 20.5 28Z" stroke-width="1.6"/>'
            '<path d="M5 9H20.5Q23 9 23 11.5V27M22.2 9.8L27 5" stroke-width="1.2"/>'
            f'</g><g transform="translate(4.54 10.1) scale(0.56)" fill="{color}">'
            f'<path d="M5 18V14C5 7.9 9.9 3 16 3S27 7.9 27 14V18" fill="none" stroke="{color}" stroke-width="2.8" stroke-linecap="round"/>'
            '<rect x="3" y="16" width="4.5" height="11" rx="2"/><rect x="24.5" y="16" width="4.5" height="11" rx="2"/>'
            '<path d="M10 25V14H15V16.4H12.4V18.3H14.8V20.7H12.4V25Z"/>'
            '<path fill-rule="evenodd" d="M17 25V14H19.5C22.1 14 23.2 15.4 23.2 17.5C23.2 19 22.6 20 21.5 20.6L23.5 25H20.8L19.4 21.2V25ZM19.4 16.3V18.8H19.6C20.5 18.8 20.9 18.3 20.9 17.5S20.5 16.3 19.6 16.3Z"/></g>')


def flatten_svg(body):
    """Export absolute paths only, including embedded comparison-sheet SVGs."""
    import re
    token = re.compile(r'[A-Za-z]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?')
    def number(value):
        return f'{value:.5f}'.rstrip('0').rstrip('.') if value else '0'
    def transformed_path(data, scale, tx, ty):
        parts = token.findall(data)
        output = []
        command = None
        i = 0
        counts = {'M':2, 'L':2, 'H':1, 'V':1, 'C':6, 'S':4, 'Q':4, 'T':2}
        while i < len(parts):
            if parts[i].isalpha():
                command = parts[i]
                i += 1
            if command == 'Z' or command == 'z':
                output.append('Z')
                command = None
                continue
            if command not in counts:
                raise ValueError(f'Unsupported SVG command: {command}')
            n = counts[command]
            values = [float(x) for x in parts[i:i+n]]
            if len(values) != n:
                raise ValueError('Incomplete SVG path')
            i += n
            if command == 'H': values = [values[0]*scale+tx]
            elif command == 'V': values = [values[0]*scale+ty]
            else: values = [x*scale+(tx if j%2 == 0 else ty) for j,x in enumerate(values)]
            output.append(command+' '.join(number(x) for x in values))
            if command == 'M': command = 'L'
        return ' '.join(output)
    paths = []
    presentation = ('fill','stroke','stroke-width','stroke-linecap','stroke-linejoin','fill-rule','opacity','fill-opacity','stroke-opacity')
    def walk(node, scale=1, tx=0, ty=0, inherited=None):
        style = dict(inherited or {'fill':'#000000', 'stroke':'none'})
        for k in presentation:
            if k in node.attrib: style[k] = node.get(k)
        for op,args in re.findall(r'(translate|scale)\(([^)]+)\)',node.get('transform','')):
            values = [float(x) for x in re.split(r'[,\s]+',args.strip())]
            if op == 'translate':
                tx += scale*values[0]
                ty += scale*(values[1] if len(values)>1 else 0)
            else:
                if len(values)>1 and values[0] != values[1]: raise ValueError('Nonuniform scale')
                scale *= values[0]
        tag = node.tag.split('}')[-1]
        if tag == 'title': return
        if tag == 'svg':
            vx,vy,vw,vh = [float(v) for v in node.get('viewBox').split()]
            width,height = float(node.get('width')),float(node.get('height'))
            fit = min(width/vw,height/vh)
            tx += scale*(float(node.get('x','0'))+(width-vw*fit)/2-vx*fit)
            ty += scale*(float(node.get('y','0'))+(height-vh*fit)/2-vy*fit)
            scale *= fit
        if tag in ('g','svg'):
            for child in node: walk(child,scale,tx,ty,style)
            return
        if tag == 'rect':
            x,y,w,h,r = [float(node.get(k,'0')) for k in ('x','y','width','height','rx')]
            r = min(r,w/2,h/2)
            data = (f'M{x+r} {y}H{x+w-r}C{x+w-r+r*0.55228475} {y} {x+w} {y+r-r*0.55228475} {x+w} {y+r}V{y+h-r}'
                    f'C{x+w} {y+h-r+r*0.55228475} {x+w-r+r*0.55228475} {y+h} {x+w-r} {y+h}H{x+r}C{x+r-r*0.55228475} {y+h} {x} {y+h-r+r*0.55228475} {x} {y+h-r}'
                    f'V{y+r}C{x} {y+r-r*0.55228475} {x+r-r*0.55228475} {y} {x+r} {y}Z')
        elif tag == 'path': data = node.get('d')
        else: raise ValueError(f'Unsupported SVG shape: {tag}')
        if 'stroke-width' in style: style['stroke-width'] = number(float(style['stroke-width'])*scale)
        paths.append(ET.tostring(ET.Element('path',{'d':transformed_path(data,scale,tx,ty),**style}),encoding='unicode'))
    walk(ET.fromstring('<g>'+body+'</g>'))
    return ''.join(paths)


def document(body, width=512, height=512, title='FlowRate Server'):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}"><title>{title}</title>{flatten_svg(body)}</svg>\n')


def text_path(text, size, x, baseline, color, centered=False):
    ctx = cairo.Context(cairo.ImageSurface(cairo.FORMAT_ARGB32, 1, 1))
    ctx.select_font_face('Noto Sans', cairo.FONT_SLANT_NORMAL, cairo.FONT_WEIGHT_BOLD)
    ctx.set_font_size(size)
    if centered:
        ext = ctx.text_extents(text)
        x -= ext.x_bearing + ext.width / 2
    ctx.move_to(x, baseline)
    ctx.text_path(text)
    commands = []
    for kind, values in ctx.copy_path():
        letter = {cairo.PATH_MOVE_TO: 'M', cairo.PATH_LINE_TO: 'L',
                  cairo.PATH_CURVE_TO: 'C', cairo.PATH_CLOSE_PATH: 'Z'}[kind]
        commands.append(letter + ' '.join(f'{v:.3f}' for v in values))
    return f'<path fill="{color}" d="{" ".join(commands)}"/>'

def save(name, body, width=512, height=512):
    path = SVG / f'{name}.svg'
    path.write_text(document(body, width, height), encoding='utf-8')
    ET.parse(path)
    return path


def render(path, width, height=None, destination=None):
    height = height or width
    surface = cairo.ImageSurface(cairo.FORMAT_ARGB32, width, height)
    handle = Rsvg.Handle.new_from_file(str(path))
    viewport = Rsvg.Rectangle()
    viewport.x, viewport.y, viewport.width, viewport.height = 0, 0, width, height
    assert handle.render_document(cairo.Context(surface), viewport)
    target = destination or PNG / f'{path.stem}-{width}.png'
    surface.write_to_png(str(target))
    return target


def embed(name, x, y, width, height, prefix):
    element = ET.parse(SVG / f'{name}.svg').getroot()
    body = ''.join(ET.tostring(child, encoding='unicode') for child in element
                   if not child.tag.endswith('title'))
    return (f'<svg x="{x}" y="{y}" width="{width}" height="{height}" '
            f'viewBox="{element.get("viewBox")}">{body}</svg>')


def android_vector(body):
    """Convert our path/rect artwork to a native vector with Android's adaptive safe area."""
    import re
    android_ns = 'http://schemas.android.com/apk/res/android'
    def attrs(values):
        return ' '.join(f'android:{key}="{value}"' for key, value in values.items())
    def convert(node):
        tag = node.tag.split('}')[-1]
        if tag == 'g':
            values = {}
            for op, args in re.findall(r'(translate|scale)\(([^)]+)\)', node.get('transform', '')):
                numbers = [float(n) for n in re.split(r'[,\s]+', args.strip())]
                if op == 'translate':
                    values.update(translateX=numbers[0], translateY=numbers[1] if len(numbers) > 1 else 0)
                else:
                    values.update(scaleX=numbers[0], scaleY=numbers[1] if len(numbers) > 1 else numbers[0])
            return f'<group {attrs(values)}>' + ''.join(convert(c) for c in node) + '</group>'
        if tag not in ('path', 'rect'):
            raise ValueError(f'Unsupported vector shape: {tag}')
        d = node.get('d')
        if tag == 'rect':
            x, y, w, h, r = [float(node.get(k, '0')) for k in ('x', 'y', 'width', 'height', 'rx')]
            d = (f'M{x+r} {y}H{x+w-r}C{x+w-r+r*0.55228475} {y} {x+w} {y+r-r*0.55228475} {x+w} {y+r}V{y+h-r}'
                 f'C{x+w} {y+h-r+r*0.55228475} {x+w-r+r*0.55228475} {y+h} {x+w-r} {y+h}H{x+r}C{x+r-r*0.55228475} {y+h} {x} {y+h-r+r*0.55228475} {x} {y+h-r}'
                 f'V{y+r}C{x} {y+r-r*0.55228475} {x+r-r*0.55228475} {y} {x+r} {y}Z')
        values = {'pathData': d}
        for source, target, default in (('fill', 'fillColor', '#000000'), ('stroke', 'strokeColor', 'none')):
            color = node.get(source, default)
            # The only inherited fill is on logo groups; pass it explicitly below.
            values[target] = '#00000000' if color == 'none' else '#FF' + color.lstrip('#')
        for source, target in (('stroke-width', 'strokeWidth'), ('stroke-linecap', 'strokeLineCap'), ('stroke-linejoin', 'strokeLineJoin')):
            if node.get(source): values[target] = node.get(source)
        if node.get('fill-rule') == 'evenodd': values['fillType'] = 'evenOdd'
        return '<path ' + attrs(values) + '/>'
    tree = ET.fromstring('<g>' + body + '</g>')
    def explicit_fills(node, inherited='#000000'):
        fill = node.get('fill', inherited)
        if node.tag.split('}')[-1] in ('path', 'rect') and 'fill' not in node.attrib:
            node.set('fill', fill)
        for child in node: explicit_fills(child, fill)
    explicit_fills(tree)
    return (f'<vector xmlns:android="{android_ns}" android:width="108dp" android:height="108dp" '
            'android:viewportWidth="768" android:viewportHeight="768">'
            '<group android:translateX="128" android:translateY="128">' + convert(tree) + '</group></vector>\n')


def main():
    SVG.mkdir(exist_ok=True)
    PNG.mkdir(exist_ok=True)
    app_sizes = (16, 24, 32, 48, 64, 128, 256, 512, 1024)
    for name, radius in (('app-icon', 112), ('app-icon-square', 0)):
        path = save(name, f'<rect width="512" height="512" rx="{radius}" fill="{PURPLE}"/>' + cube(True))
        for size in app_sizes if radius else (1024,): render(path, size)
    for name, color in (('purple', PURPLE), ('white', '#FFFFFF'), ('dark', DARK)):
        path = save(f'mark-{name}', cube() if name == 'purple' else monochrome(color))
        for size in (64, 128, 256, 512): render(path, size)
        path = save(f'tray-{name}', tray(color), 32, 32)
        for size in (16, 20, 24, 32, 48): render(path, size)
    favicon = save('favicon', '<rect width="32" height="32" rx="7" fill="' + PURPLE + '"/>'
                   + '<g transform="translate(2 2) scale(0.875)">' + tray('#FFFFFF') + '</g>', 32, 32)
    for size in (16, 32, 48): render(favicon, size)
    for name, color in (('light', DARK), ('dark', '#FFFFFF')):
        # The icon itself has no caption; the horizontal logo names the product.
        body = '<g transform="scale(0.5)">' + cube() + '</g>'
        body += text_path('FlowRate', 110, 274, 132, color)
        body += text_path('SERVER', 43, 280, 199, PURPLE if name == 'light' else '#B8B3FF')
        path = save(f'wordmark-on-{name}', body, 850, 256)
        render(path, 1700, 512)
    Image.open(PNG / 'app-icon-256.png').save(ROOT / 'flowrate.ico', sizes=[(s,s) for s in (16,24,32,48,64,128,256)])
    Image.open(PNG / 'favicon-48.png').save(ROOT / 'favicon.ico', sizes=[(s,s) for s in (16,32,48)])
    android = ROOT / 'Android'
    for folder in ('drawable', 'values', 'mipmap-anydpi-v26', 'mipmap-anydpi-v33'):
        (android / folder).mkdir(parents=True, exist_ok=True)
    (android / 'drawable/ic_launcher_foreground.xml').write_text(android_vector(cube(True)))
    (android / 'values/icon_colors.xml').write_text('<resources><color name="flowrate_icon_background">#FF6C63FF</color></resources>\n')
    for version in (26, 33):
        mono = '<monochrome>' + android_vector(monochrome('#FFFFFF')) + '</monochrome>' if version == 33 else ''
        (android / f'mipmap-anydpi-v{version}/ic_launcher.xml').write_text(
            '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">'
            '<background android:drawable="@color/flowrate_icon_background"/>'
            '<foreground android:drawable="@drawable/ic_launcher_foreground"/>' + mono + '</adaptive-icon>\n')
    for density, size in (('mdpi',48),('hdpi',72),('xhdpi',96),('xxhdpi',144),('xxxhdpi',192)):
        folder = android / f'mipmap-{density}'
        folder.mkdir(exist_ok=True)
        render(SVG / 'app-icon-square.svg', size, destination=folder / 'ic_launcher.png')
    board = ('<rect width="1200" height="900" fill="#F5F4F8"/>'
             '<rect x="610" y="90" width="550" height="770" rx="24" fill="#232323"/>')
    board += text_path('FlowRate Server', 30, 40, 56, DARK)
    for name, x, y, size in (('app-icon',40,110,240),('mark-purple',330,110,240),('mark-white',650,110,240),('app-icon-square',930,140,180)):
        board += embed(name,x,y,size,size,name)
    board += text_path('Tray: 16 / 24 / 32 / 48 px',21,40,470,DARK)
    for i, size in enumerate((16,24,32,48)):
        board += embed('tray-purple',40+i*95,398,size,size,f'light{i}')
        board += embed('tray-white',650+i*95,398,size,size,f'dark{i}')
    board += embed('wordmark-on-light',35,530,535,161,'wordlight')
    board += embed('wordmark-on-dark',630,530,510,154,'worddark')
    board += embed('favicon',40,750,64,64,'fav')
    board += text_path('Favicon',21,126,790,DARK)
    preview = ROOT / 'preview.svg'
    preview.write_text(document(board,1200,900,'FlowRate Server selected rounded cube'))
    render(preview,1200,900,ROOT / 'preview.png')
    for path in ROOT.rglob('*.svg'):
        ET.parse(path)
        content = path.read_text()
        assert 'currentColor' not in content and '<mask' not in content and '<clipPath' not in content, path
    for path in android.rglob('*.xml'): ET.parse(path)
    for path in ROOT.rglob('*.png'):
        with Image.open(path) as image: image.verify()
    assert Image.open(ROOT / 'flowrate.ico').ico.sizes() == {(s,s) for s in (16,24,32,48,64,128,256)}
    assert Image.open(ROOT / 'favicon.ico').ico.sizes() == {(s,s) for s in (16,32,48)}
    print(f'Validated {len(list(ROOT.rglob("*.svg")))} SVG, {len(list(ROOT.rglob("*.png")))} PNG, two ICO, and Android XML.')


if __name__ == '__main__':
    main()
