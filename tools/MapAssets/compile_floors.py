"""Compile native, player-free floor surveys into the map atlas. Requires Pillow."""
import argparse
from array import array
import base64
from collections import deque
import json
import math
from pathlib import Path
import struct
import zlib
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]

def neighbors(i, w, n):
    if i >= w: yield i - w
    if i + w < n: yield i + w
    if i % w: yield i - 1
    if i % w + 1 < w: yield i + 1

def components(mask, w, heights=None):
    ids = array("H", [0]) * len(mask)
    groups = []
    for seed in range(len(mask)):
        if not mask[seed] or ids[seed]: continue
        number = len(groups) + 1
        queue = deque([seed]); ids[seed] = number
        cells = array("I")
        while queue:
            i = queue.popleft(); cells.append(i)
            for j in neighbors(i, w, len(mask)):
                if mask[j] and not ids[j] and (heights is None or abs(heights[i]-heights[j]) <= 45): ids[j] = number; queue.append(j)
        groups.append(cells)
    return ids, groups

def walkable(mask, w, plan):
    if not plan.get("heights"): return mask
    heights = array("h");heights.frombytes(base64.b64decode(plan["heights"]))
    ids, groups = components(mask,w,heights)
    keep=set(); h=len(mask)//w
    for seed in plan["seeds"]:
        x=round((seed["x"]-plan["left"])/plan["step"]);y=round((plan["top"]-seed["z"])/plan["step"])
        maximum=round(3/plan["step"]); choices=[]
        for yy in range(max(0,y-maximum),min(h,y+maximum+1)):
            for xx in range(max(0,x-maximum),min(w,x+maximum+1)):
                i=yy*w+xx
                if ids[i] and abs(heights[i]/100-seed["y"])<.9:
                    choices.append(((xx-x)**2+(yy-y)**2+(heights[i]/100-seed["y"])**2/plan["step"]**2,ids[i]))
        if choices:keep.add(min(choices)[1])
    for seed in plan.get("cameraSeeds",[]):
        x=round((seed["x"]-plan["left"])/plan["step"]);y=round((plan["top"]-seed["z"])/plan["step"])
        maximum=round(2/plan["step"]);choices=[]
        for yy in range(max(0,y-maximum),min(h,y+maximum+1)):
            for xx in range(max(0,x-maximum),min(w,x+maximum+1)):
                i=yy*w+xx;drop=seed["y"]-heights[i]/100
                if ids[i] and .7<drop<35:
                    choices.append(((xx-x)**2+(yy-y)**2+.15*(drop-3.2)**2/plan["step"]**2,ids[i],i))
        if choices:
            _,region,i=min(choices);keep.add(region)
            if plan["room"]=="Hcz079" and "CONT CHAMBER" in seed["name"]:
                plan["marks"].append(dict(key="SCP-079 chamber",x=plan["left"]+(i%w+.5)*plan["step"],z=plan["top"]-(i//w+.5)*plan["step"]))
    if not keep:raise ValueError("No reachable floor seed for "+plan["room"])
    for i in range(len(mask)):
        if ids[i] not in keep:mask[i]=0
    return mask

def clean(mask, w, step):
    _, groups = components(mask, w)
    for cells in groups:
        if len(cells) * step * step < .2:
            for i in cells: mask[i] = 0
    inverse = bytearray(0 if value else 255 for value in mask)
    _, holes = components(inverse, w)
    for cells in holes:
        if len(cells) * step * step > .4: continue
        if any(i < w or i >= len(mask) - w or i % w in (0, w - 1) for i in cells): continue
        for i in cells: mask[i] = 255
    return mask

def nearest(ids, x, y, w, h, radius):
    for distance in range(radius + 1):
        found = []
        for yy in range(max(0, y-distance), min(h, y+distance+1)):
            for xx in range(max(0, x-distance), min(w, x+distance+1)):
                i = yy*w+xx
                if ids[i]: found.append(((xx-x)**2+(yy-y)**2, i))
        if found: return min(found)[1]
    return None

def pack_string(text):
    data = text.encode(); value = len(data); output = bytearray()
    while value >= 128: output.append((value & 127) | 128); value >>= 7
    return output + bytes([value]) + data

def rectangles(mask, w, ids=None):
    result = {}; active = {}
    for y in range(len(mask)//w):
        current = {}; x = 0
        while x < w:
            begin = x; value = mask[y*w+x]; region = ids[y*w+x] if ids is not None else 0
            x += 1
            while x < w and mask[y*w+x] == value and (ids is None or ids[y*w+x] == region): x += 1
            if not value or ids is not None and not region: continue
            key = (begin,x,value,region)
            if key in active: rect = active.pop(key); rect[3] += 1
            else: rect = [begin,y,x-begin,1,value]
            current[key] = rect
        for key, rect in active.items(): result.setdefault(key[3],[]).append(rect)
        active = current
    for key, rect in active.items(): result.setdefault(key[3],[]).append(rect)
    return result

def pack_rectangles(parts):
    return struct.pack("<i",len(parts))+b"".join(struct.pack("<4HB",*part) for part in parts)

def compile_one(plan):
    w, h, step = plan["width"], plan["height"], plan["step"]
    mask = clean(walkable(bytearray(base64.b64decode(plan["mask"])), w, plan), w, step)
    image = Image.frombytes("L",(w,h),bytes(mask)); bounds = image.getbbox()
    if bounds is None: raise ValueError("Empty native floor: "+plan["room"])
    x0,y0,x1,y1 = max(0,bounds[0]-1),max(0,bounds[1]-1),min(w,bounds[2]+1),min(h,bounds[3]+1)
    plan = dict(plan,left=plan["left"]+x0*step,top=plan["top"]-y0*step)
    image = image.crop((x0,y0,x1,y1)); w,h=image.size;mask=bytearray(image.tobytes())
    plan["width"],plan["height"]=w,h
    split = Image.frombytes("L", (w,h), bytes(mask)).point(lambda value: 255 if value else 0)
    draw = ImageDraw.Draw(split)
    for p in plan["portals"]:
        x, y = (p["x"]-plan["left"])/step, (plan["top"]-p["z"])/step
        dx, dy = p["dx"]*p["span"]/2/step, -p["dz"]*p["span"]/2/step
        draw.line((x-dx,y-dy,x+dx,y+dy), fill=0,width=3)
    ids, groups = components(split.tobytes(), w)
    for cells in groups:
        if len(cells)*step*step < .3:
            for i in cells: ids[i] = 0
    # Find an interior label anchor in each actual chamber, away from walls.
    distances = array("H", [0]) * len(mask)
    for i in range(len(mask)):
        if not ids[i]: continue
        a = distances[i-1] if i%w and ids[i-1]==ids[i] else 0
        b = distances[i-w] if i>=w and ids[i-w]==ids[i] else 0
        distances[i] = min(a,b)+1
    for i in range(len(mask)-1,-1,-1):
        if not ids[i]: continue
        a = distances[i+1] if i%w+1<w and ids[i+1]==ids[i] else 0
        b = distances[i+w] if i+w<len(mask) and ids[i+w]==ids[i] else 0
        distances[i] = min(distances[i],a+1,b+1)
    centers = {}
    for number, cells in enumerate(groups,1):
        if not any(ids[i] for i in cells): continue
        cx=sum(i%w for i in cells)/len(cells);cy=sum(i//w for i in cells)/len(cells)
        centers[number] = max(cells,key=lambda i:(distances[i],-((i%w-cx)**2+(i//w-cy)**2)))
    marks = []; used = set()
    for mark in plan["marks"]:
        x=round((mark["x"]-plan["left"])/step);y=round((plan["top"]-mark["z"])/step)
        i=nearest(ids,x,y,w,h,round(4/step))
        if i is None: continue
        region=ids[i]; key=(region,mark["key"])
        if key in used: continue
        used.add(key);marks.append(dict(region=region,key=mark["key"],cell=i))
    for mark in marks:
        i=centers[mark["region"]] if sum(m["region"]==mark["region"] for m in marks)==1 else mark["cell"]
        mark["x"]=plan["left"]+(i%w+.5)*step;mark["z"]=plan["top"]-(i//w+.5)*step
    data=bytearray(pack_string(plan["room"]))
    data.extend(struct.pack("<6f2i",plan["reference"],plan["minY"],plan["maxY"],plan["left"],plan["top"],step,w,h))
    data.extend(ids.tobytes())
    base=rectangles(mask,w).get(0,[]); selected=rectangles(mask,w,ids)
    data.extend(pack_rectangles(base));data.extend(struct.pack("<i",len(selected)))
    for region,parts in sorted(selected.items()):data.extend(struct.pack("<H",region));data.extend(pack_rectangles(parts))
    data.extend(struct.pack("<i",len(marks)))
    for m in marks:data.extend(struct.pack("<Hff",m["region"],m["x"],m["z"]));data.extend(pack_string(m["key"]))
    info={k:plan[k] for k in ["room","reference","minY","maxY","width","height","step"]}
    info.update(quads=len(base),maximum_highlight_quads=max(map(len,selected.values()),default=0),regions=len(centers),marks=[{k:m[k] for k in ["region","key","x","z"]} for m in marks],occupied=sum(bool(v) for v in mask))
    preview=Image.new("RGB",(w,h),(11,20,29));blue=Image.new("RGB",(w,h),(82,125,149));preview.paste(blue,mask=Image.frombytes("L",(w,h),bytes(mask)))
    return data,info,preview

def main():
    parser=argparse.ArgumentParser();parser.add_argument("survey",type=Path);parser.add_argument("--preview",type=Path);args=parser.parse_args()
    plans=json.loads(args.survey.read_text());output=bytearray(struct.pack("<ii",2,len(plans)));metadata=[]
    if args.preview: args.preview.mkdir(parents=True,exist_ok=True)
    for number,plan in enumerate(plans):
        data,info,preview=compile_one(plan);output.extend(data);metadata.append(info)
        if args.preview: preview.save(args.preview/(str(number)+"-"+plan["room"]+".png"))
        print(plan["room"],round(plan["reference"],2),info["regions"],len(info["marks"]),flush=True)
    compressor=zlib.compressobj(9,wbits=-15);folder=ROOT/"src/Methadrenaline.Server/Map"
    (folder/"native-room-floors.deflate").write_bytes(compressor.compress(output)+compressor.flush())
    (folder/"native-room-floors.json").write_text(json.dumps(dict(game_version="14.2.9",source="Native collider and camera geometry; no player data",floors=metadata),ensure_ascii=False,indent=2)+"\n")

if __name__=="__main__": main()
