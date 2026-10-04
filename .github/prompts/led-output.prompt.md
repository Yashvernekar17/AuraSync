---
description: Trace and change AuraSync LED layout, RGB processing, controller protocol, or serial output.
mode: agent
---

Implement this LED-output change:

${input:task:Describe the layout, color-processing, or controller behavior}

Trace only the affected end-to-end path: profile/layout UI → sampling/index mapping → RGB processing/preview → binary frame transport → service validation/protocol encoding/serial lifecycle.

- Keep LED numbering, geometry, sampled colors, and serial frame ordering consistent.
- Preserve pure black when changing brightness or enhancement behavior; retain serial bandwidth limits and stale-frame dropping.
- Follow the protocol and channel order implemented in source; do not infer hardware behavior. Preserve saved profile compatibility.
- Update `docs/user-guide.md` when controller setup, wiring, protocol, or operating steps change. The documented example is an Arduino Nano, WS2812 strip, D6, Adalight, 108 LEDs, and 115200 baud; keep its sketch and profile settings aligned.
- Add focused tests for changed rules and run the relevant Angular/.NET checks. Never claim physical hardware validation unless it was performed.

Summarize the data-path change, touched files, checks, and hardware verification status.
