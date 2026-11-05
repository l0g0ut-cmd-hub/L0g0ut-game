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


def fog_overlay(background: Image.Image, on: bool, fr: tuple[float,float,float,float]) -> None:
    # Exact approved artwork: same 325x152 canvas and original fixed plaque.
    x0,y0,x1,y1=map(round,fr)
    sprite=load(UI/'selection'/('fog_toggle_inner_on.png' if on else 'fog_toggle_inner_off.png'))
    assert sprite.size==(325,152), 'Fog states must use the same 325x152 canvas'
    background.alpha_composite(sprite.resize((x1-x0,y1-y0),Image.Resampling.LANCZOS),(x0,y0))


def difficulty_outline(im: Image.Image, rect, selected: int) -> None:
    # Same straight-line additive rim currently drawn in DrawDifficultySelectionGlow.
    x0,y0,x1,y1=rect
    for passno in range(4,-1,-1):
        offset=1+passno*2.5
        thickness=(2.7 if passno==0 else 3.2+passno)
        alpha=(.92 if passno==0 else .075+.04/passno)*.84
        color=(255,round(255*(.91 if passno==0 else .73)),
               round(255*(.54 if passno==0 else .23)),round(255*alpha))
        d=Image.new('RGBA',im.size)
        p=ImageDraw.Draw(d,'RGBA')
        xx0,yy0,xx1,yy1=x0-offset,y0-offset,x1+offset,y1+offset
        solid(p,(xx0,yy0,xx1,yy0+thickness),color)
        solid(p,(xx0,yy1-thickness,xx1,yy1),color)
        solid(p,(xx0,yy0,xx0+thickness,yy1),color)
        solid(p,(xx1-thickness,yy0,xx1,yy1),color)
        im.alpha_composite(d)


def generate(out: Path) -> dict:
    out.mkdir(parents=True,exist_ok=True)
    code=SOURCE.read_text(encoding='utf-8')
    cards=rects_in_source(code,'RedesignDifficultyCards')
    fog=single_rect(code,'DifficultyFog')
    ok=single_rect(code,'DifficultyConfirm')
    back=single_rect(code,'DifficultyBack')
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
            matte=load(UI/'selection/difficulty_plaque_matte_fix.png')
            assert matte.size==(305,147)
            picture.alpha_composite(matte,(1467,669))
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