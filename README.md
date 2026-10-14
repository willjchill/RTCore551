# RTCore FPGA Ray-Tracing Accelerator

This repository is organized as a Unity project and an FPGA project:

- **Unity software lives under `Assets/`.** `Assets/RTCoreAPI` lets you author and
  export scenes; `Assets/RayTracing` contains the ray-tracing software and
  Episode02 reference.
- **`RTCore/` is the FPGA accelerator**: RTL, board integration, simulation,
  and Xilinx implementation scripts.

Unity is the scene editor and scene-data source. Episode02 is an executable
GPU ray-tracing reference: its C# code builds the BVH and supplies scene data,
and its HLSL shader generates rays, traverses the BVH, intersects triangles,
and shades the image. RTCore is intended to consume the same versioned scene
input and perform those stages on the FPGA. Unity remains the GUI, not the
ray-tracing implementation being accelerated.

## Clone and open in Unity

This repository already contains the Unity project folders (`Assets`,
`Packages`, and `ProjectSettings`). Clone the repository, then in Unity Hub
choose **Add/Open project from disk** and select the repository root. There is
no need to create a separate empty project first.

To copy the Unity code into an existing empty 3D Unity project instead, copy
`Assets/RTCoreAPI` and `Assets/RayTracing` into that project's `Assets` folder,
add the `com.unity.mathematics` package (version `1.2.6`), and select the
Built-in Render Pipeline (Episode02 uses `Camera.OnRenderImage`, which is not
supported by URP or another Scriptable Render Pipeline). The repository is
already configured for Built-in rendering.

## Project layout

```text
Assets/                              Unity-imported software and content
  RTCoreAPI/                         scene authoring/export integration
    Scripts/
      Editor/                        legacy-format parity fixture only
      Runtime/                       scene components, fixed-point packing,
                                     BVH build, serialization, UART draft,
                                     and image comparison helpers
    Generated/                       retained legacy exporter demo materials and meshes
  RayTracing/                        ray-tracing software/reference
    Reference/
      Episode02/                      pinned Seb Lague reference source, in
                                       Unity's import tree; MIT license included
        Scripts/                      C#, HLSL shaders, and data types

Packages/                             Unity package manifest and lock file
ProjectSettings/                      Unity project configuration
RTExport/                             deterministic .rtscene/.mem test inputs

RTCore/                               FPGA accelerator implementation
  xilinx_settings/                    maintained Vivado settings and scripts
  logs/                               local logs/reports (ignored)
  verilog/                            reusable Verilog/SystemVerilog modules
  top/                                FPGA top-level and board integration
  testbench/                          simulation/Verilator test code

tools/scene_exporter_bundle/           standalone Python scene checker and docs
```

`Assets/` is Unity's asset import tree: scripts, shaders, materials, and meshes
placed there are imported by Unity. `Assets/RTCoreAPI/` is the Unity-facing
scene-export API; top-level `RTCore/` is not Unity content and is reserved for
the FPGA accelerator. Generated Unity state, local logs, simulator waveforms,
and FPGA build products are excluded by `.gitignore`.

## Run the RayTracing reference

Open the repository in Unity and let it resolve packages and compile scripts.
If Unity was already open when you cloned the project, restart the Editor once
so it loads the Built-in Render Pipeline setting.

Use the single scene-creation command **RayTracing > Create > Replace Current
Scene With Demo**. After confirmation, it replaces the open scene, creates a
small deterministic room with a floor, spheres, a wall, and a camera, then
saves the result as `Assets/Scenes/RayTracingDemo.unity`, overwriting that
asset if it already exists. If the current scene has unsaved edits, Unity asks
whether to save them before replacement. Open the **Game** view and press
**Play** to render. The demo has accumulation enabled by default so later
frames progressively average new stochastic samples; switch it off in the
camera's `RayTracingManager` to see a single-frame result.

To inspect a stage, select the camera and change **Vis Mode** on its
`RayTracingManager`; see [What the visualization modes show](#what-the-visualization-modes-show).
Press **Space** to reset the progressive render and **S** to save a screenshot
under Unity's persistent data path.

The legacy RTCoreAPI exporter is separate and does not create another demo
scene. To try it, add `RTSceneController` to a root object and `RTSceneObject`
components to geometry in a scene you authored; then use the component context
menu's **Export Legacy RTS1 Prototype** or **Validate Scene (log only)**. This
old format already contains a CPU-built BVH and is not the intended
raw-geometry FPGA contract. The **RTCoreAPI > Write Legacy RTS1 Parity Fixture**
menu only writes a small format test fixture; it does not create or alter a
Unity scene.

Validate the checked-in sample export and run the checker tests with Python:

```powershell
python tools/scene_exporter_bundle/tools/rtscene_inspect.py check RTExport/scene.rtscene
python tools/scene_exporter_bundle/tools/rtscene_inspect.py selftest
```

## Episode02 software pipeline: from scene to pixels

The imported files under `Assets/RayTracing/Reference/Episode02` come from
[Sebastian Lague's Episode02 branch](https://github.com/SebLague/Ray-Tracing/tree/Episode02),
pinned to commit `b8fb046816874451942db17b5d0e0e4003b2531c`. They include the
BVH builder and traversal, ray-triangle calculations, ray generation, and
bounce/shading code. The upstream [MIT license](Assets/RayTracing/Reference/Episode02/LICENSE)
is included.

The C# BVH builder is CPU code; the ray calculations and pixels are produced
by HLSL in Unity's GPU pipeline. There is no hidden `DoShading()` API: the
shader explicitly computes each collision, chooses the next path direction,
updates light throughput, and writes the pixel. The manager is the bridge
between the Unity scene, CPU BVH builder, GPU buffers, and shader. This is
executable reference code, but not a standalone CPU renderer.

### 1. Unity objects become per-mesh acceleration data

Each reference object has a [`Model` component](Assets/RayTracing/Reference/Episode02/Scripts/Types/Model.cs)
containing a `MeshFilter` and a
[`RayTracingMaterial`](Assets/RayTracing/Reference/Episode02/Scripts/Types/RayTracingMaterial.cs).
[`RayTracingManager.InitFrame`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L149)
finds those models. Its
[`CreateAllMeshData`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L255)
builds a BVH only the first time it sees a mesh, then reuses that mesh's
triangle/node offsets for other objects sharing it. Each `MeshInfo` retains
the instance's transforms and material. The vertex positions, per-vertex
normals, and triangle indices are read in
[`BVH`](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L15).

### 2. CPU constructs a BVH (bounding volume hierarchy)

A BVH is a tree of axis-aligned bounding boxes (AABBs). Each leaf points to a
small range of triangles; each internal node points to two children. A ray
that misses a node's box can skip every triangle underneath it. It is a
candidate-pruning structure: it does **not** determine the final surface color.

Episode02 computes a triangle's centroid and min/max bounds in
[`BVH`'s constructor](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L15).
[`ChooseSplit`](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L120)
tries five candidate split positions on each of the three axes.
[`EvaluateSplit` and `NodeCost`](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L150)
estimate the cost of the left and right child as
`(x*y + x*z + y*z) * triangleCount`, which is proportional to box surface
area times the triangles tested there. [`Split`](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L60)
recurses only if the two child costs beat the unsplit parent's cost (and depth
is below 32). This surface-area heuristic (SAH) balances tree traversal work
against triangle tests; it is not simply a fixed-depth or median split.

The manager concatenates each unique mesh's triangles and nodes into GPU
buffers. The node start indices are local to a mesh's BVH; `nodeOffset` and
`triOffset` in the model data locate that mesh's ranges in the combined
buffers. This happens on the CPU in
[`CreateAllMeshData`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L255),
then buffers and camera/shading settings are bound in
[`InitFrame` and `SetShaderParams`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L149).

### 3. A pixel generates a primary camera ray

Unity invokes [`RayTracingManager.OnRenderImage`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L83)
after the camera renders. In ray-tracing mode the manager suppresses ordinary
mesh rasterization and uses `Graphics.Blit` to run the ray-tracing shader over
the output texture. The HLSL fragment function
[`frag`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L423)
runs once per output pixel.

The pixel's UV coordinate maps to a point on a virtual focus plane in
[`frag`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L423).
The ray begins at the camera and points toward that point. `DefocusStrength`
randomly moves the origin on a lens disk (depth of field); `DivergeStrength`
randomly jitters the focus point (anti-aliasing blur). Both are zero in the
provided demo defaults, so the primary camera ray is deterministic for a
given pixel and frame. The shader uses a PCG random-number generator for
sampling paths; when samples/bounces are enabled, the frame is part of the
seed.

### 4. Transform the ray, walk the BVH, and intersect triangles

[`CalculateRayCollision`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L292)
loops over scene models. It transforms the world-space ray into each model's
local coordinates, where the mesh BVH and triangles live. The direction is
transformed with `w = 0` (so translation does not affect it), and its
component-wise reciprocal is cached for box tests.

[`RayTriangleBVH`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L235)
uses an explicit stack. For an internal node, it tests both child AABBs with
[`RayBoundingBoxDst`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L220),
visits the nearer child first, and skips children farther than the closest hit
already found. At leaves, it tests the referenced triangles with
[`RayTriangle`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L193).

`RayTriangle` is the Möller–Trumbore intersection algorithm. It computes
triangle edges and a determinant, then solves for distance `dst` and
barycentric coordinates `u`, `v`, and `w`. A hit is in front of the ray and
inside the triangle when `dst >= 0`, each weight is nonnegative, and
`u + v <= 1` (equivalently `w >= 0`). Those weights interpolate the triangle's
three vertex normals to produce the smooth hit normal. BVH traversal and this
test answer **which surface point is closest**; they have not yet shaded it.

The geometry behind those numbers is `P(t) = O + tD` for the ray and
`Q(u,v) = A + u(B-A) + v(C-A)` for a point on triangle `(A,B,C)`. The
intersection routine solves `P(t) = Q(u,v)`; `u >= 0`, `v >= 0`, and
`u+v <= 1` mean the solution lies inside the triangle. Since the reference
normalizes the world ray direction, `t` is the ray distance in world space.
The implementation checks `determinant >= 1E-8`, so it accepts only the
triangle winding facing the ray; this is not a two-sided intersection test.

### 5. The shader explicitly traces bounces and computes radiance

[`Trace`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L330)
is the shading/path-tracing algorithm. It loops from bounce zero through
`MaxBounceCount`, so the maximum setting is the number of *additional*
collisions allowed after the primary ray. At each hit:

1. Read that model's `RayTracingMaterial`.
2. Add emitted color (`emissionColour * emissionStrength`) multiplied by the
   path's current `rayColour` to `incomingLight`.
3. Randomly choose whether the path is specular using
   `specularProbability`. Form a diffuse direction by adding a random unit
   vector to the surface normal; form a mirror direction with HLSL's
   `reflect(rayDir, normal)`. `smoothness` blends toward the mirror direction
   when the specular event is selected.
4. Multiply `rayColour` by the material's diffuse or specular tint. This is
   the path throughput: the fraction of later light that contributes back to
   the camera.
5. Apply Russian roulette: with probability related to the largest throughput
   channel, stop a path that contributes little; surviving paths are
   reweighted so the estimate stays unbiased.

If a ray misses, [`GetEnvironmentLight`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L172)
returns the procedural ground/sky gradient (and its sun highlight) times the
current throughput, and the path ends. Thus “shading” is not an external
Unity API call; the HLSL operations above are the shading. In particular,
Episode02 does not launch a separate direct-light/shadow ray at every hit.
Its light comes from emissive hits and the environment reached by the sampled
bounce path. The demo's material/display components do not make Unity's
standard rasterizer calculate the reference ray color.

Finally, [`frag`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L423)
averages the `NumRaysPerPixel` path results and writes the RGB pixel.

### 6. Accumulation averages successive noisy images

With `accumulate` enabled and `VisMode.Default`, the manager renders a new
stochastic sample image, saves the previous accumulated texture, and invokes
[`Accumulate.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/Accumulate.shader#L43).
That shader computes the running mean
`newMean = oldMean * (1 - 1/(n+1)) + newSample/(n+1)`.
Each frame's random seed changes, so one frame can be noisy but the average
typically becomes steadier over time. This texture is image history, **not**
BVH construction, ray history, or a separate lighting algorithm. Set
`accumulate` off for a fresh single-frame image; debug visualization modes also
bypass accumulation. The shader applies `saturate` to the averaged color, so
values are clamped to `[0, 1]` in this path. Press **Space** to reset the frame
counter and sampling.

### What the visualization modes show

`VisMode` is declared in
[`RayTracingManager`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L7),
and [`TraceDebugMode`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L385)
traces **one primary ray** and colors that pixel with a diagnostic instead of
running the multi-bounce `Trace` shading above:

| Mode | Meaning | Reading the image |
|---|---|---|
| **Default (0)** | Full sampled multi-bounce rendering | Material emission and environment light accumulated through paths. |
| **Triangle Test Count (1)** | Number of ray/triangle tests for that pixel's primary ray | Darker means fewer tests; brightness rises relative to `triTestVisScale`; red means the count reached/exceeded the scale. |
| **Box Test Count (2)** | Number of child AABB tests for that primary ray | Same scale-to-brightness convention using `boxTestVisScale`; red means at/above the scale. |
| **Distance (3)** | Distance from the ray origin to the closest hit | Scalar grayscale relative to `distanceTestVisScale`; a miss is black. |
| **Normal (4)** | Closest hit's interpolated surface normal | Normal components remapped from `[-1,1]` to `[0,1]` as RGB; a miss is black. |

These are not pictures of BVH boxes or the BVH itself. The two count modes
visualize how much traversal work a pixel's ray did; the distance and normal
modes visualize the closest hit produced by traversal. The
`DEBUG_VIS` shader keyword selects this path in
[`frag`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L423).

### What runs where—and what it does not do

| Stage | Implementation | Result |
|---|---|---|
| Gather mesh data and build per-mesh SAH BVH | C# on CPU: [`RayTracingManager.CreateAllMeshData`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L255), [`BVH`](Assets/RayTracing/Reference/Episode02/Scripts/BVH.cs#L15) | Triangle, node, and model buffers |
| Generate camera rays; traverse AABBs; intersect triangles | HLSL on GPU: [`RayTracer.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L193) | Closest hit, normal, material, traversal counts |
| Bounce sampling, throughput, environment/emission shading | HLSL on GPU: [`Trace`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L330) | Estimated radiance per sample |
| Average rays and optionally frames | HLSL on GPU: [`frag`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L423), [`Accumulate.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/Accumulate.shader#L43) | Final display pixel |

The imported algorithm is the behavior to study and compare. It is not yet
wired to the RTCoreAPI fixed-point packet: that API still exports a different
legacy format, discussed below.

## Current implementation status

- `Assets/RayTracing/Reference/Episode02` is the runnable GPU reference. Its
  C# builder uses a surface-area heuristic to build one BVH per unique mesh;
  the shader consumes those BVHs and produces the rendered image.
- `Assets/RTCoreAPI` currently exports the separate `RTS1` format: it flattens
  scene triangles into world space, quantizes them, builds a global
  centroid-median BVH on the CPU, and serializes that prebuilt BVH. Its triangle
  records do not include Episode02's vertex normals. `RTS1` is therefore a
  useful exporter/checker prototype, **not** the final FPGA input contract for
  accelerating BVH construction.
- `tools/scene_exporter_bundle` contains the Python scene-format checker.
- `RTCore/` is laid out for FPGA RTL and verification, but no HDL source is
  present yet.

## Planned architecture and paper relationship

The architectural reference is Jae-Ho Nah et al., [“RayCore: A ray-tracing
hardware architecture for mobile devices”](https://doi.org/10.1145/2601097.2601368),
*ACM Transactions on Graphics* 33 (2014), DOI
10.1145/2601097.2601368. The supplied copy is
[raycore_paper_ref.pdf](raycore_paper_ref.pdf). Relevant parts are Sections
3.2–3.6 (RTU, ray generation, unified traversal/intersection, memory, shading),
Section 3.7 (tree-building unit), and Section 3.8 (API), printed pages 5–8.

The paper is a design reference, **not a description of this repository's
current implementation**. Its prototype uses a hardware tree-building unit
(TBU), ray-tracing units (RTUs), SAH **kd-trees**, and a Whitted-style renderer
with reflection, refraction, shadows, and Phong/texture shading. Its API is
described as OpenGL ES 1.1-like extensions for static and dynamic geometry; it
does not specify a portable binary scene packet or the exact wire protocol we
need.

This project deliberately keeps the Episode02 behavior as the golden-rendering
target: its builder creates per-unique-mesh SAH **BVHs**, and its shader does
stochastic diffuse/specular path tracing with emission and an environment
gradient. Those are different acceleration and shading algorithms from the
paper. The paper's useful architectural ideas here are therefore the split
between building acceleration structures and tracing rays, a unified
traversal/intersection datapath, independent ray work, and deliberate memory
organization. The FPGA must build the same BVH and reproduce the Episode02
shader behavior to claim parity; substituting the paper's kd-tree or Whitted
shader would be a different renderer, not a conforming acceleration.

Planned processing flow:

```text
Unity scene
  -> RTCoreAPI snapshots canonical raw scene input (no BVH)
  -> the same decoded input is given to:
       software golden adapter: build Episode02-compatible per-mesh BVHs
                              -> generate / trace / shade / accumulate
       RTCore:                 build compatible per-mesh BVHs in hardware
                              -> generate / trace / shade / accumulate
  -> compare build reports, optional ray/hit diagnostics, and linear pixels
```

At a high level, the hardware separates into:

1. **Input and scene memory:** validate a versioned scene packet; keep unique
   mesh geometry, per-instance transforms/materials, camera, and render
   settings addressable by the build and render stages.
2. **BVH build unit:** build the Episode02-compatible BVH from each unique
   mesh's local-space positions, normals, and triangle indices. Reuse a mesh's
   BVH for its instances. Transform-only changes update instance data; changed
   mesh geometry invalidates and rebuilds that mesh's BVH.
3. **Ray work scheduler and RTU datapath:** create the same primary samples as
   Episode02, traverse the BVH, test triangles, and feed closest-hit data to
   the shading/path state machine. A unified traversal/intersection pipeline
   and independent ray contexts are paper-inspired options; their exact
   parallelism and memory layout remain RTL design decisions.
4. **Shading, accumulation, and readback:** reproduce Episode02 bounce
   direction selection, throughput/Russian-roulette termination, emission,
   environment contribution, per-pixel ray averaging, and optional progressive
   frame accumulation. Return pixels and requested intermediate diagnostics.

The paper's fixed-function Whitted shading block cannot simply be copied for
this target. It must be adapted to Episode02's path-tracing rules (or replaced
with an explicitly programmable shading contract) and validated against the
software reference. Episode02 does not implement the paper's refraction, hard
shadow, Phong, or texture-mapping features; those are not silently part of this
parity contract.

## Planned scene/render API contract (logical V2)

The interface below defines the **logical data and operations** both backends
must consume and produce. It is a design contract, not an implemented C# API or
binary ABI: record byte offsets, endianness, transport framing, and hardware
limits still need to be specified before implementation. In particular, the
legacy `RTGO`/`RTFR` UART sketch is not this API.

### Request

One `RenderSceneV2` request is a complete scene snapshot; it contains a version,
request ID, and these records. Initially, a scene edit sends a new snapshot
rather than a partial update. Incremental static/dynamic updates can be added
later as a separately versioned extension, after full-snapshot parity works.

| Record | Required fields and meaning |
|---|---|
| `Mesh[]` | Stable mesh ID; local-space vertex positions and vertex normals in matching vertex order; triangle index array in Unity mesh order. Shared Unity meshes appear once. |
| `Instance[]` | Stable instance ID; mesh ID; local-to-world and world-to-local transforms; the complete Episode02 material (`colour`, `emissionColour`, `specularColour`, `emissionStrength`, `smoothness`, `specularProbability`, `flag`). `flag` values are `None=0`, `CheckerPattern=1`, `InvisibleLight=2`. The transforms are per instance, not baked into mesh vertices. |
| `Camera` | Camera-to-world transform, image width/height, vertical field of view, focus distance, defocus strength, and divergence strength. Orthographic cameras are outside the current reference contract. |
| `RenderSettings` | Episode02 `maxBounceCount` (0–32; the shader loops bounce indices from zero through this value), `numRaysPerPixel` (1–64; zero must be rejected because the shader divides by this value), `useSky`, `sunColour`, `sunFocus`, `sunIntensity`, visualization mode and its triangle/box/distance scales, plus the deterministic frame index and RNG algorithm version. Visualization values are `Default=0`, `TriangleTestCount=1`, `BoxTestCount=2`, `Distance=3`, `Normal=4`. Episode02 seeds each pixel with `pixelIndex + Frame * 719393` in [`RayTracer.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L430); V2 must preserve the seed derivation and random-number routine. Accumulation enable/reset and prior accumulated-frame count are explicit so a comparison uses the same work. |
| `NumericFormat` | Encoding version and fixed-point scales. Initial proposal: positions/transforms and scalar distances/settings signed Q16.16; normals signed Q1.15; color and unit-interval material parameters UNORM8; indices/IDs unsigned 32-bit; counts and enums fixed-width unsigned fields. |

The transform convention is Unity world space and mesh-local space: each
instance carries matrices sufficient to map points in both directions, and the
reference and FPGA must use the same quantized matrix values. Mesh-local
normals are the supplied vertex normals; the normal transformation and
normalization must match the shader. The initial UNORM8 color proposal only
represents channel values in [0,1]; inputs outside that range (for example HDR
emission colors) must be rejected or trigger an explicit format revision, not
silently clamped. Color-space conversion, matrix record ordering, overflow
policy, and exact quantization/rounding must be frozen in the serializer/parser
tests, not left to backend defaults.

### Response

Each request returns a `RenderResultV2` with the matching request ID, status,
image dimensions, per-pixel RGB radiance before display conversion, and
completed sample/frame counts. Progressive accumulation must match Episode02's
running average and saturation in [`Accumulate.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/Accumulate.shader#L43).
When requested, it also returns:

- **Build report:** per-mesh node/leaf counts, maximum depth, and build status.
- **Ray diagnostics:** pixel/sample/bounce identity, generated ray origin and
  direction, hit/miss, hit instance and triangle, hit distance/position/normal,
  and AABB/triangle test counts.
- **Comparison metadata:** numeric-format version and any invalid, unsupported,
  or out-of-range input reported as an explicit error (not a successful-looking
  fallback image).

The observable debug modes should correspond to these diagnostics: box-test
count, triangle-test count, closest-hit distance, and hit normal. Their visual
color mapping is a presentation choice; the underlying values are the
comparison data.

### Contract invariants

- Both backends decode the **same serialized, quantized input**. Do not render
  the golden image from higher-precision Unity-only values.
- The request contains raw geometry and no prebuilt BVH. Both implementations
  build the agreed per-mesh BVH. Build policy, leaf/depth limits, tie-breaking,
  and degenerate-triangle handling must be made deterministic and recorded.
- Mesh vertex/index order, instance transforms, material fields, camera
  sampling, random seed, bounce limit, and accumulation state are identical.
- Compare discrete results exactly where deterministic (IDs, hit/miss, counts
  when traversal order is fixed); compare fixed-point-derived positions,
  directions, radiance, and pixels against documented numeric tolerances.
- Keep the legacy RTS1 serializer and checker as regression coverage for that
  old format only. Do not relabel RTS1 as V2 or send it to the new full-pipeline
  design.

### What code does today

There are currently two separate paths, neither implementing logical V2:

1. **Episode02 GPU reference:** [`RayTracingManager.CreateAllMeshData`](Assets/RayTracing/Reference/Episode02/Scripts/RayTracingManager.cs#L255)
   deduplicates meshes, builds its float BVHs on the CPU, and uploads triangle,
   node, and model buffers. [`RayTracer.shader`](Assets/RayTracing/Reference/Episode02/Scripts/Shaders/RayTracer.shader#L330)
   performs the GPU tracing and shading. This is the executable baseline, not a
   CPU golden renderer and not yet a consumer of a serialized RTCoreAPI packet.
2. **Legacy RTCoreAPI exporter:** [`RTSceneBuilder.Build`](Assets/RTCoreAPI/Scripts/Runtime/RTSceneBuilder.cs#L10)
   transforms triangles into world space, quantizes them, builds a CPU-side
   global median BVH, and [`SceneSerializer.Serialize`](Assets/RTCoreAPI/Scripts/Runtime/SceneSerializer.cs#L101)
   writes that BVH into RTS1. It omits the per-vertex normals and mesh/instance
   separation needed by V2.

[`UartLink`](Assets/RTCoreAPI/Scripts/Runtime/UartLink.cs#L1) is a disabled-by-
default draft that wraps an RTS1 file in `RTGO` and expects an `RTFR` RGB24
frame. It is not a hardware driver or the planned V2 transport. No V2
serializer, quantized software adapter, FPGA packet reader, builder RTL, or
rendering RTL exists yet. The code comments and names in the current exporter
identify its RTS1 behavior as legacy; that path must remain separate until the
V2 implementation is verified.

## Verification plan

1. Run the Episode02 scene and validate its BVH and diagnostic visualizations.
2. Freeze the V2 binary ABI, implement its serializer/parser, and test the
   fixed-point encodings, ranges, rounding, and error responses.
3. Implement a software adapter that consumes that exact packet and reproduces
   Episode02's BVH and render behavior; then build the matching FPGA BVH and
   rendering datapath.
4. Compare BVH/build statistics, ray/intersection intermediates, and final
   pixels on small deterministic scenes; document numerical tolerances and
   intentional differences.
