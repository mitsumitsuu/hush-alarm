"""Render the app's simple vector headphone mark into required native icon formats."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
image = Image.new('RGB', (1024, 1024), '#126f6c')
draw = ImageDraw.Draw(image)
draw.arc((240, 215, 784, 845), 180, 360, fill='white', width=62)
draw.rounded_rectangle((205, 500, 345, 790), radius=62, fill='white')
draw.rounded_rectangle((679, 500, 819, 790), radius=62, fill='white')
assets = root / 'assets'
assets.mkdir(exist_ok=True)
image.save(assets / 'HushAlarm.ico', sizes=[(16,16), (32,32), (48,48), (64,64), (256,256)])
catalog = root / 'ios/App/Assets.xcassets'
icons = catalog / 'AppIcon.appiconset'
icons.mkdir(parents=True, exist_ok=True)
image.save(icons / 'AppIcon.png')
(catalog / 'Contents.json').write_text(json.dumps({'info': {'author':'xcode', 'version':1}}), encoding='utf-8')
(icons / 'Contents.json').write_text(json.dumps({'images':[
    {'filename':'AppIcon.png', 'idiom':'universal', 'platform':'ios', 'size':'1024x1024'}
], 'info': {'author':'xcode', 'version':1}}), encoding='utf-8')
print('Generated native app icons')
