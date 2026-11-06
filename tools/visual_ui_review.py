#!/usr/bin/env python3
"""BladePath CI UI-composition previews, made from the project's *actual* art.

Not an Android runtime capture. This deterministic compositor reproduces the
selection screen's source rectangles and current IMGUI overlays, providing a
reviewable regression gallery and geometry diagnostics after every commit.
It deliberately fails if art/hitboxes are missing or visibly out of bounds.
"""
from __future__ import annotations
import argparse
import json
import math
import re
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[1]
UI = ROOT / 'Assets/_Project/Resources/UI'
SOURCE = ROOT / 'Assets/_Project/Scripts/Menu/MainMenuController.cs'
ART_W, ART_H = 1824, 862


def rects_in_source(code: str, name: str) -> list[tuple[float, float, float, float]]:
    block = re.search(r'\b' + re.escape(name) + r'\s*=\s*\{(.*?)\};', code, re.S)
    if block is None:
        raise AssertionError(f'Missing C# rectangle array: {name}')
    matches = re.findall(r'SelRect\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*\)',block.group(1))
    if len(matches) != 3:
        raise AssertionError(f'Expected three rectangles for {name}, got {len(matches)}')
    return [tuple(map(float, x)) for x in matches]


def single_rect(code: str, name: str) -> tuple[float, float, float, float]:
    match = re.search(r'\b'+re.escape(name)+r'\s*=\s*SelRect\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f\s*\)', code)
    if not match: raise AssertionError(f'Missing C# rectangle: {name}')
    return tuple(map(float,match.groups()))


def load(path: Path) -> Image.Image:
    if not path.is_file(): raise AssertionError(f'Missing visual asset: {path}')
    return Image.open(path).convert('RGBA')


def over(dst: Image.Image, src: Image.Image, xy: tuple[int,int]) -> None:
    dst.alpha_composite(src, xy)


def switch_art(src: Image.Image, diameter: int) -> Image.Image:
    # Corresponds to FogIcon() in MainMenuController; Unity GetPixels is bottom-up.
    w,h=src.size
    size=min(h-4,round(w*.46))
    cx=round(w*.317);cy=round(h*.49)
    x0=max(0,min(cx-size//2,w-size))
    y0=max(0,min(cy-size//2,h-size))
    cropped=src.crop((x0,h-y0-size,x0+size,h-y0))
    alpha=Image.new('L',(size,size),0)
    pix=alpha.load()
    radius=size*.47
    for y in range(size):
        for x in range(size):
            dist=math.hypot(x-(size-1)*.5,y-(size-1)*.5)
            f=max(0,min(1,(radius+.5-dist)/3))
            pix[x,y]=round(255*f*f*(3-2*f))
    cropped.putalpha(alpha)
    return cropped.resize((diameter,diameter),Image.Resampling.LANCZOS)


def solid(draw: ImageDraw.ImageDraw,rect,fill):
    draw.rectangle(tuple(round(n) for n in rect),fill=fill)


def validate_fog_art_geometry() -> None:
    """Prevent a black JPEG matte or 1-2 pixel nudge between ON and OFF.

    These are project assets, independent of any simulated rendering.
    Compare full alpha silhouettes and anchored ornament edges, not just sizes.
    """
    off=load(UI/'selection/fog_toggle_inner_off.png')
    on=load(UI/'selection/fog_toggle_inner_on.png')
    if off.size != (325,152) or on.size != off.size:
        raise AssertionError('Fog toggle PNG canvases must match exactly (325x152)')
    a_off=off.getchannel('A')
    a_on=on.getchannel('A')
    bounds_off=a_off.getbbox()
    bounds_on=a_on.getbbox()
    if bounds_off != bounds_on:
        raise AssertionError('Fog switch outer silhouette shifts between OFF and ON: '
                             f'{bounds_off} -> {bounds_on}')
    # Transparent exterior on all four corners; dark rectangular JPG mats
    # have alpha=255 here and should fail immediately.
    for label,im in [('OFF',off),('ON',on)]:
        alpha=im.getchannel('A')
        for xy in [(0,0),(324,0),(0,151),(324,151)]:
            if alpha.getpixel(xy) > 16:
                raise AssertionError(f'{label} fog PNG has opaque outer background at {xy}')
    # The decorative perimeter itself does not move; require aligned alpha
    # gradients along the entire canvas (slider and cloud RGB may change).
    from PIL import ImageChops, ImageStat
    alpha_difference=ImageChops.difference(a_off,a_on)
    pixels_diff=sum(1 for p in alpha_difference.getdata() if p > 20)
    if pixels_diff > 90:
        raise AssertionError(f'Fog switch silhouette/ornament jumps: {pixels_diff} alpha pixels differ')
    print('FOG ART OK: 325x152, aligned silhouette',bounds_off,
          'alpha difference pixels=',pixels_diff)


def fog_overlay(background: Image.Image, on: bool, fr: tuple[float,float,float,float]) -> None:
    # Exact approved artwork: same 325x152 canvas and original fixed plaque.
    x0,y0,x1,y1=map(round,fr)
    sprite=load(UI/'selection'/('fog_toggle_inner_on.png' if on else 'fog_toggle_inner_off.png'))
    assert sprite.size==(325,152), 'Fog states must use the same 325x152 canvas'
    background.alpha_composite(sprite.resize((x1-x0,y1-y0),Image.Resampling.LANCZOS),(x0,y0))


# Reproduce the BP-051 GENERATED Unity halo, not the deleted straight
# rectangle. The geometry below is shared with MainMenuController.cs.
# This preview is still an artwork composition, NOT an Android screenshot.
from functools import lru_cache

@lru_cache(maxsize=1)
def difficulty_halo() -> Image.Image:
    w,h=180,264
    outline=[
        (.075,.042),(.38,.042),(.50,.010),(.62,.042),
        (.925,.042),(.975,.075),(.975,.920),(.925,.955),
        (.62,.955),(.50,.993),(.38,.955),(.075,.955),
        (.025,.920),(.025,.075),
    ]
    segments=[
        ((a[0]*(w-1),a[1]*(h-1)),(b[0]*(w-1),b[1]*(h-1)))
        for a,b in zip(outline,outline[1:]+outline[:1])
    ]
    halo=Image.new('RGBA',(w,h),(0,0,0,0))
    pixels=halo.load()
    for y in range(h):
        for x in range(w):
            nearest_sq=1e12
            for (ax,ay),(bx,by) in segments:
                dx=bx-ax;dy=by-ay
                proj=max(0.,min(1.,((x-ax)*dx+(y-ay)*dy)/max(.0001,dx*dx+dy*dy)))
                dist_sq=(x-ax-proj*dx)**2+(y-ay-proj*dy)**2
                nearest_sq=min(nearest_sq,dist_sq)
            soft=math.exp(-nearest_sq/36.)*.42
            rim=math.exp(-nearest_sq/1.8)*.58
            alpha=min(1.,soft+rim)*(.92+.08*0.0)
            pixels[x,y]=(255,round(255*(.78+.17*rim)),
                         round(255*(.36+.26*rim)),round(255*alpha))
    return halo

def difficulty_outline(im: Image.Image, rect, selected: int) -> None:
    x0,y0,x1,y1=rect
    margin=6
    scaled=difficulty_halo().resize(
        (round(x1-x0+margin*2),round(y1-y0+margin*2)),
        Image.Resampling.BILINEAR)
    im.alpha_composite(scaled,(round(x0-margin),round(y0-margin)))

def generate(out: Path) -> dict:
    out.mkdir(parents=True,exist_ok=True)
    code=SOURCE.read_text(encoding='utf-8')
    cards=rects_in_source(code,'RedesignDifficultyCards')
    fog=single_rect(code,'DifficultyFog')
    ok=single_rect(code,'DifficultyConfirm')
    back=single_rect(code,'DifficultyBack')
    validate_fog_art_geometry()
    frame=load(UI/'selection/screen_difficulty_redesign.jpg')
    assert frame.size==(ART_W,ART_H), f'Difficulty art is {frame.size}, expected {(ART_W,ART_H)}'
    errors=[]
    if not ('DrawDifficultySelectionGlow(r, s)' in code): errors.append('Difficulty overlay not connected to selection')
    if not ('DrawFogToggleArt(' in code): errors.append('Fog toggle not connected')
    for i,rect in enumerate(cards):
        x0,y0,x1,y1=rect
        if not (10<x0<x1<ART_W-10 and 80<y0<y1<ART_H-40): errors.append('Card %d out of artwork bounds'%i)
    for n,rect in [('fog',fog),('confirm',ok),('back',back)]:
        x0,y0,x1,y1=rect
        if not (0<=x0<x1<=ART_W and 0<=y0<y1<=ART_H): errors.append(n+' hitbox outside image')
    screenshots=[]
    for selected,slug in enumerate(('easy','warrior','master')):
        for on in (False,True):
            picture=frame.copy()
            difficulty_outline(picture,cards[selected],selected)
            fog_overlay(picture,on,fog)
            name=f'difficulty_{slug}_fog_{"on" if on else "off"}.jpg'
            picture.convert('RGB').save(out/name,quality=88,subsampling=0)
            screenshots.append(name)
    # A geometry guide is also saved for diagnosing the *actual code* coordinates.
    guide=frame.copy();d=ImageDraw.Draw(guide,'RGBA')
    for idx,r in enumerate(cards):
        d.rectangle(tuple(map(round,r)),outline=(32,238,252,255),width=4)
    for r in (ok,back,fog):d.rectangle(tuple(map(round,r)),outline=(240,20,208,255),width=3)
    guide.convert('RGB').save(out/'layout_debug.jpg',quality=88)
    screenshots.append('layout_debug.jpg')
    # Poster-based main-menu approximation (video isn't changed/played by this tool).
    poster=ROOT/'Assets/_Project/Art/menu_poster_first.jpg'
    if poster.is_file():
        p=load(poster)
        canvas=Image.new('RGBA',(1920,864),(10,9,15,255))
        ratio=min(canvas.width/p.width,canvas.height/p.height)
        w,h=round(p.width*ratio),round(p.height*ratio)
        x=(1920-w)//2;y=(864-h)//2
        over(canvas,p.resize((w,h),Image.Resampling.LANCZOS),(x,y))
        # Deliberately preserve side bars as a layout diagnostic rather than
        # reconstruct procedurally generated Perlin noise incorrectly.
        d=ImageDraw.Draw(canvas,'RGBA')
        if x>0:
            solid(d,(x-3,0,x,864),(225,170,75,240))
            solid(d,(x+w,0,x+w+3,864),(225,170,75,240))
        canvas.convert('RGB').save(out/'main_menu_video_bounds.jpg',quality=88)
        screenshots.append('main_menu_video_bounds.jpg')
    traveller=UI/'selection/screen_traveller.jpg'
    if traveller.is_file():
        tr=load(traveller)
        tr.convert('RGB').save(out/'traveller_unselected.jpg',quality=88)
        screenshots.append('traveller_unselected.jpg')
    # A single handy gallery for checking all six state combinations at a glance.
    tilew=640; tileh=round(ART_H*tilew/ART_W)
    board=Image.new('RGB',(tilew*3,tileh*2),(7,7,9))
    for i,name in enumerate(screenshots[:6]):
        im=Image.open(out/name).convert('RGB').resize((tilew,tileh),Image.Resampling.LANCZOS)
        board.paste(im,((i//2)*tilew,(i%2)*tileh))
    board.save(out/'contact_sheet.jpg',quality=89)
    screenshots.append('contact_sheet.jpg')
    report={'type':'source-art composition preview, NOT screenshots from Android or Unity runtime',
            'artwork_resolution':[ART_W,ART_H],
            'card_hitboxes':[list(r) for r in cards],
            'fog_hitbox':list(fog),'button_hitboxes':[list(ok),list(back)],
            'artifacts':screenshots,'errors':errors}
    (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    (out/'README.md').write_text(
        '# BladePath — UI review gallery\n\nGenerated from the exact UI source assets and C# rectangle values at this commit.\n'
        '\n**These are deterministic composition previews, NOT screenshots of an Android device or running Unity build.**\n'
        '\nView `contact_sheet.jpg` for all three difficulties and both fog states. '
        '`layout_debug.jpg` shows the actual hitbox coordinates from `MainMenuController.cs`.\n',encoding='utf-8')
    if errors: raise AssertionError('; '.join(errors))
    print('UI PREVIEW OK:',len(screenshots),'frames; three difficulties x two fog states; assets', ART_W, ART_H)
    return report


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--out',default='build/ui-review')
    args=parser.parse_args()
    generate(Path(args.out))