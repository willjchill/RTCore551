# RTCore551: Unity scene + exporter

This folder contains the current RTS1 scene-export prototype. It builds a 3D
scene in Unity and packs fixed-point triangles, a CPU-built BVH, materials,
lights, and camera data. The checker verifies this format and traversal.

**RTS1 is not the final input contract for the full-pipeline accelerator.** It
includes a BVH built on the host and omits Episode02's per-vertex normals, so
the FPGA cannot use it to build its own matching BVH and reproduce Episode02
shading. The intended replacement is a versioned raw-geometry packet consumed
by both the Episode02-compatible reference and the FPGA.

The Python checker and the C# exporter produce the same RTS1 bytes for the
reference scene (parity test prints `IDENTICAL`), and this format's BVH
traversal matches brute force on the demo scene. This validates the prototype
format, not the future Episode02-to-FPGA pipeline.

## What's in this folder

```
README.md
RTExport/                   <- the demo scene, already exported, ready to use
  scene.rtscene               binary scene (format below)
  scene_report.txt            triangle counts, materials, memory
  mem/*.mem                   same data as hex words for $readmemh in testbenches
Assets/RTCoreAPI/           <- copy this into a Unity project
  Scripts/Runtime/            the exporter code
  Scripts/Editor/             the RTCoreAPI legacy-format parity fixture menu
tools/rtscene_inspect.py    <- checks a .rtscene file, no Unity needed
```

If you only do RTL work you just need `RTExport/`, this README and `tools/`.

## Checking a scene file (no Unity needed)

```
python3 tools/rtscene_inspect.py check RTExport/scene.rtscene
```

It checks the CRC and format, checks the BVH is built correctly, and shoots 300 test rays to make sure walking the
BVH gives the same answer as testing every triangle. You should see `BVH structure OK` and `0 mismatches`.
`python3 tools/rtscene_inspect.py selftest` runs the tool's own tests.

## Running it in Unity

1. Open the Unity project (or copy `Assets/RTCoreAPI` into a compatible Unity project) and wait for it to compile.
2. Create your scene using **RayTracing > Create > Replace Current Scene With Demo** if the RayTracing reference assets are installed, or author a scene and add `RTSceneObject` components to its meshes.
3. Add `RTSceneController` to a root object and assign the scene root and camera.
4. In the Inspector, use the controller's context menu to **Validate Scene (log only)** or **Export Legacy RTS1 Prototype**.
5. Files show up in `<your unity project>/RTExport/`. RTS1 includes a host-built BVH and is a legacy format prototype, not the full-pipeline FPGA input.

Optional parity test: run **RTCoreAPI > Write Legacy RTS1 Parity Fixture** in Unity, then

```
python3 tools/rtscene_inspect.py check-reference <unity project>/RTExport/reference.rtscene
```

It should say `IDENTICAL`. That means the C# code writes exactly what this README describes.

## How the scene gets built

```
RTSceneRoot            (RT Scene Controller: resolution, bounces, sky, output folder)
 ├─ Room, Props        (empty objects just to organize)
 │   └─ objects        (MeshFilter + RT Scene Object, one material each)
 └─ PointLight         (RT Light Source)
Main Camera            (any perspective camera)
```

The exporter goes through the objects in order, turns every mesh into triangles in world space, rounds them to
fixed point, removes duplicate materials, builds the BVH and sorts the triangles so each leaf is a continuous range.

- One material per object. If you want two materials, use two child objects.
- Only triangles. Every triangle counts as double sided, so winding order doesn't matter.
- Imported models need Read/Write enabled in their import settings.
- Zero area triangles are dropped (the report says how many).
- Coordinates are Unity world space (left handed, Y up), unchanged.

## Number formats

| What | Format | Notes |
|---|---|---|
| Positions (triangles, lights, BVH boxes) | Q16.16 signed 32 bit | range about ±32768, exporter throws an error if something is outside |
| Camera ray vectors | Q4.28 signed 32 bit | more fraction bits so `x * dirDX` stays accurate over hundreds of pixels |
| Colors, reflectivity, transparency, smoothness | unsigned 8 bit | 255 means 1.0 |
| Index of refraction | unsigned 8 bit, Q2.6 | 64 = 1.0, 96 = 1.5, max 3.98 |
| Light intensity | unsigned 8 bit, Q4.4 | 16 = 1.0, max 15.94 |

The BVH boxes are made from the already rounded vertices, so a box always contains its triangles exactly.
Rounding is half away from zero.

## File format (`.rtscene`, little endian)

Sections come one after the other with no padding:

| Section | Size | Contents |
|---|---|---|
| Header | 32 B | see next table |
| Camera | 48 B | `origin` (x,y,z Q16.16), `dir00`, `dirDX`, `dirDY` (each x,y,z Q4.28), all `i32` |
| Sky | 8 B | horizon `r,g,b,0` then zenith `r,g,b,0` |
| Lights | 16 B each | position x,y,z (`i32`), then `r,g,b,intensity` (`u8`) |
| Materials | 16 B each | see below |
| Triangles | 40 B each | `v0`, `v1`, `v2` (9 × `i32`), `materialId` (`u16`), `flags` (`u16`, bit 0 = double sided, always 1) |
| BVH nodes | 32 B each | `min` x,y,z (`i32`), `max` x,y,z (`i32`), `offset` (`u32`), `count` (`u16`), reserved (`u16`) |
| CRC32 | 4 B | CRC-32 of all the bytes before it (same as Python `zlib.crc32`) |

Header:

| Offset | Type | Field |
|---|---|---|
| 0 | char[4] | `"RTS1"` |
| 4 | u16 | version (1) |
| 6 | u16 | header size (32) |
| 8 | u16 | width |
| 10 | u16 | height |
| 12 | u8 | position fraction bits (16) |
| 13 | u8 | camera fraction bits (28) |
| 14 | u8 | max bounces |
| 15 | u8 | reserved |
| 16 | u32 | number of triangles |
| 20 | u16 | number of materials |
| 22 | u16 | number of lights |
| 24 | u32 | number of BVH nodes |
| 28 | u32 | flags (0) |

Material (16 bytes): bytes 0-3 albedo `r,g,b,0` · 4-7 emission `r,g,b,0` (color × strength, clamped) ·
8 reflectivity · 9 transparency · 10 ior (Q2.6) · 11 smoothness · 12-15 reserved (0, maybe a texture id later).

In the `.mem` files every record is split into 32 bit words, one per line, in file order. A triangle is 10 lines:
`v0.x v0.y v0.z v1.x v1.y v1.z v2.x v2.y v2.z {flags[31:16], materialId[15:0]}`.

## For the RTL people

**Ray generation.** Pixel column `x`, row `y`, (0,0) is the top left pixel, we use pixel centers:

```
origin = camera.origin                       Q16.16
dir    = dir00 + x*dirDX + y*dirDY           Q4.28, not normalized
```

The image plane is 1 unit in front of the camera, so the forward part of `dir` is exactly 1. You don't need to
normalize `dir` for Möller–Trumbore, `t` just comes out in units of `|dir|`. Only normalize where shading needs it.

**BVH.**
- `nodes[0]` is the root.
- `count == 0`: internal node, the children are `nodes[offset]` and `nodes[offset + 1]` (always next to each other).
- `count > 0`: leaf, its triangles are `triangles[offset ... offset + count - 1]`.
- We split on the widest axis at the median, leaves have at most 4 triangles. The demo scene tree is 9 levels deep,
  so a traversal stack of 16 to 20 entries is plenty.

## Memory

A scene needs 40 B per triangle + 32 B per BVH node + 16 B per material. Roughly 60 B per triangle in total.
The demo scene (598 triangles, 427 nodes) is 36.8 KiB. The exporter warns above 256 KiB, but that number is just a
placeholder until we know the real BRAM plan. The Arty A7-100T has about 4.8 Mbit (around 600 KB) of block RAM,
worth double checking in the Digilent datasheet.

## Stuff we should agree on

1. Fixed point only (no floats in the RTL).
2. Q4.28 for the camera vectors. With plain Q16.16 the pixels at the edge of a 320 wide image would be about 0.7 pixel off.
3. Triangle meshes only (spheres are just lots of triangles). Using real spheres would need a new record type.
4. Static scene, one material per object, no textures.
5. The 256 KiB budget.

## Notes and limits

- `UartLink.cs` is a draft for the UART milestone and is switched off by default, because `System.IO.Ports`
  doesn't exist in a fresh Unity project and it would break the compile. To use it: set Api Compatibility Level to
  `.NET Framework` and add `RTCORE_UART` to the Scripting Define Symbols (Project Settings > Player > Other Settings).
  The protocol in that file (`"RTGO"` + length + scene file out, `"RTFR"` + width + height + RGB bytes back) is only a
  suggestion, change it however you like.
- `ImageDiff.cs` compares two PNGs pixel by pixel and writes a diff image that is black wherever they match. This is
  William's idea for checking the FPGA output against the reference render. The reference renderer itself (the
  Sebastian Lague repo) is not included here.
- The demo spheres have a winding order that was picked just for how they look in Unity. If one looks inside out
  in Unity you can ignore it, the ray tracer doesn't care.
- Orthographic cameras are not supported.
