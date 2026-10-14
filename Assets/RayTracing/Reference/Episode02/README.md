# SebLague Ray Tracing — Episode02 reference

This directory preserves the relevant upstream Episode02 scripts and shaders
under Unity's `Assets` folder so Unity imports them with the project. The
reference source is kept separate from our implementation and should not be
modified in place; make adaptations in our own ray-tracing code.

- Upstream: <https://github.com/SebLague/Ray-Tracing/tree/Episode02>
- Imported branch commit: `b8fb046816874451942db17b5d0e0e4003b2531c`
- License: see [`LICENSE`](./LICENSE) (MIT, copyright Sebastian Lague, 2023)
- Shader filename: `Scripts/Shaders/RayTracer.shader`

The imported source includes the BVH builder, ray tracing manager and data
types, shader helper, the ray-tracing HLSL shader, and frame accumulation
shader. The C# code builds the BVH and supplies GPU buffers; HLSL performs ray
generation, traversal, intersections, and shading. The top-level FPGA
accelerator is organized under `RTCore/`.

## Local integration notes

The repository adds **RayTracing > Create > Replace Current Scene With Demo**.
After confirmation, it replaces the active scene with a small deterministic
reference scene and saves it as `Assets/Scenes/RayTracingDemo.unity`. This
integration needs Unity's Built-in Render Pipeline because the upstream
manager uses `Camera.OnRenderImage`; it does not run unchanged under URP.

Small integration fixes are applied to the imported code for defined no-hit
behavior and usable diagnostics: initialize the traversal-test counters and
closest-hit result, show zero hit distance when a ray misses, and reset the
accumulation frame index to zero when Space is pressed. The BVH, ray-generation,
intersection, and shading algorithms remain the pinned upstream implementation.
