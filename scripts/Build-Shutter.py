"""Deterministic original mechanical-shutter effect; no sampled third-party audio."""
import math
from pathlib import Path
import random
import struct
import wave

rate=44100
noise=random.Random(981)
samples=[]
for index in range(int(rate*.19)):
    time=index/rate
    value=0
    for start,level in [(0,.25),(.027,.19),(.085,.10)]:
        offset=time-start
        if offset>=0:
            value+=level*(noise.random()*2-1)*math.exp(-offset/.008)
            value+=level*.20*math.sin(2*math.pi*1820*offset)*math.exp(-offset/.022)
    if time>.018:value+=.025*(noise.random()*2-1)*math.exp(-(time-.018)/.04)
    samples.append(struct.pack('<h',int(max(-.6,min(.6,value))*32767)))
destination=Path(__file__).resolve().parents[1]/'ClipShot/Assets/Shutter.wav'
with wave.open(str(destination),'wb') as output:
    output.setparams((1,2,rate,0,'NONE','not compressed'))
    output.writeframes(b''.join(samples))
print('Built original 190 ms mechanical shutter WAV')
