# RTCore FPGA accelerator

`RTCore` is the hardware implementation of the ray-tracing accelerator. Unity
scene authoring and export lives under `Assets/RTCoreAPI`; software ray-tracing
reference code lives under `Assets/RayTracing/`.

The current executable reference is Episode02: CPU-side per-mesh SAH BVH
construction followed by GPU path tracing. The planned accelerator contract is
to consume the same raw, quantized geometry/material/camera input, build
compatible BVHs in hardware, and reproduce that renderer. The RayCore paper is
an architectural reference for ideas such as a separate tree builder,
ray-tracing units, and unified traversal/intersection; its kd-tree and
Whitted-rendering behavior are **not** the Episode02 parity target. See the
root [README](../README.md#planned-architecture-and-paper-relationship) for the
logical V2 request/result contract and the distinction between it and the
legacy RTS1 exporter.

No V2 packet reader, BVH builder RTL, or rendering RTL has been implemented.
Keep maintained source and scripts in the appropriate locations below:

- `xilinx_settings/`: maintained Xilinx Vivado project, synthesis, and
  place-and-route scripts/settings.
- `logs/`: local debug logs and generated reports (ignored by Git).
- `verilog/`: reusable Verilog/SystemVerilog modules.
- `top/`: FPGA top-level modules and board integration.
- `testbench/`: DUT testbenches and simulation/Verilator sources.
