# MU3 Optimizer

The mod that put your mu3 instance on steroid.

## Features

* Optimize stage rendering flow. (~40fps gain)
* Disable hard-to-notice parts in the scene. (~30fps gain)
* Disable poorly implemented ImageEffect bloom. (~10fps gain)
* Simplify UI pipeline. (~10fps gain)
* Heavily optimize and batch note rendering. (nearly x2 performance in heavy load) 
* Speed up user login requests. (x10 login speed)
* All configs are populated in patch time in case u want this on real cab.

## Default Config (Recommended)

mu3.ini

```ini
[Optimization]
NoImageBloom=1
BetterRendering=1
BoostLoginRequests=1
NoUICameraDuringPlay=1
BetterNotes=1

[Optimization.Rendering]
; Requirement: FXFPS>=BGMergeFPS>=StageFPS
StageFPS=0
BGMergeFPS=0
FXFPS=30
DisableShadows=1
```

