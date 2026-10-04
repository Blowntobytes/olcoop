# Builds olcoop.ico (16/24/32/48/64/128/256) in the style of olmod's icon: black, orange pixel letters "OL" over "COOP".
from PIL import Image
ORANGE=(255,153,0,255); SHADE=(176,96,0,255); HI=(255,200,80,255); BLACK=(0,0,0,255)
BIG={'O':["01111110","11111111","11100111","11000011","11000011","11000011","11000011","11000011","11100111","11111111","01111110"],
     'L':["11100000","11100000","11000000","11000000","11000000","11000000","11000000","11000000","11000000","11111111","11111111"]}
SMALL={'C':["01111","11111","11000","11000","11000","11111","01111"],
       'O':["01110","11111","11011","11011","11011","11111","01110"],
       'P':["11110","11111","11011","11111","11110","11000","11000"]}
def stamp(px,g,x0,y0):
    for y,row in enumerate(g):
        for x,c in enumerate(row):
            if c=='1':
                for dx,dy,col in ((1,1,SHADE),):
                    if px.get((x0+x+dx,y0+y+dy)) is None: px[(x0+x+dx,y0+y+dy)]=col
    for y,row in enumerate(g):
        for x,c in enumerate(row):
            if c=='1': px[(x0+x,y0+y)]= HI if (y==0 or x==0) and len(g)>7 else ORANGE
def render32():
    px={}
    stamp(px,BIG['O'],6,3); stamp(px,BIG['L'],17,3)
    for i,ch in enumerate("COOP"): stamp(px,SMALL[ch],2+i*7,21)
    im=Image.new('RGBA',(32,32),BLACK)
    for (x,y),c in px.items():
        if 0<=x<32 and 0<=y<32: im.putpixel((x,y),c)
    return im
im32=render32()
sizes=[16,24,32,48,64,128,256]
imgs=[]
for s in sizes:
    if s==16: imgs.append(im32.resize((16,16),Image.LANCZOS))
    elif s==24: imgs.append(im32.resize((24,24),Image.LANCZOS))
    else: imgs.append(im32.resize((s,s),Image.NEAREST))
imgs[-1].save('olcoop.ico',sizes=[(s,s) for s in sizes],append_images=imgs[:-1])
imgs[-1].save('olcoop-256.png')
