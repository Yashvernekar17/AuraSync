# AuraSync architecture

## System boundary

```text
Electron main process
  ├─ Splash/window lifecycle
  ├─ Service process lifecycle
  └─ Trusted desktop-capture IPC
       │
       ▼
Angular renderer
  ├─ Profile/settings UI
  ├─ Display capture preview
  ├─ Edge-zone geometry and RGB sampling
  ├─ LED color processing
  └─ REST control + binary RGB WebSocket
       │
       ▼
.NET 10 local service (127.0.0.1:5078)
  ├─ API → Application → Domain
  └─ Infrastructure → JSON profiles / Windows display, audio + serial
       │
       ▼
Arduino-compatible serial LED controller
```

## Component ownership

- Angular owns rendering, user interaction, selected profile/display state, preview, RGB processing, and the local service client. `custom-effect-animator.ts` generates custom RGB effects, while `led-layout.ts` owns LED perimeter geometry and canvas drawing.
- Electron owns privileged desktop concerns: windows, splash presentation, monitor-to-capture source resolution, secure IPC, service startup/shutdown, and packaging.
- The API owns HTTP/WebSocket entry points, request validation, and runtime-control orchestration.
- Application owns profile workflows and service ports.
- Domain owns profile, display, and sync models and validation-independent business concepts.
- Infrastructure owns JSON persistence and Windows display/audio/serial adapters.

## Data flows

### Startup

1. Electron shows the splash and starts the local service.
2. Electron waits for the service health endpoint.
3. Angular loads profiles, displays, and status as independent requests.
4. The renderer restores the previously selected profile and display, then signals Electron that the UI is ready.
5. Electron reveals the application window and dismisses the splash.

### Screen-to-LED output

1. Angular asks Electron IPC for the capture source corresponding to a service display ID.
2. The renderer captures a reduced-resolution video stream and samples the configured edge-zone rectangles.
3. It applies black cutoff, optional average color and color temperature, gamma, brightness, brightness cap, over-brightening, and smoothing.
4. It sends the packed RGB frame through `/api/v1/sync/frames`. If a frame is already queued, the new frame is dropped instead of accumulating latency.
5. The service validates frame size, encodes Adalight or Ardulight bytes, and writes to the selected serial port.

Configuration and control use REST. RGB data uses WebSocket and does not pass through REST.

## Persistence and compatibility

- Profiles are stored in `%LOCALAPPDATA%\LedSync\profiles.json` on Windows. Keep this path stable unless a migration is implemented.
- LED color-processing preferences are stored locally by the desktop renderer.
- The AuraSync product/workspace name is distinct from stable internal .NET assembly and namespace identifiers.
- Saved monitor IDs can become invalid after monitor topology changes. Resolve against the current display list and show an actionable state rather than silently choosing another monitor.

## Security

- The service binds to loopback.
- Electron runs a sandboxed renderer with context isolation and no Node integration.
- IPC handlers validate trusted renderer origins.
- External navigation and new windows are denied by default.
- Do not expose serial output or profile mutation to untrusted origins.

## Current limitations

- Audio capture requires an active Windows render endpoint and a compatible Windows audio environment.
- Physical controller behavior must be verified against the selected protocol, channel order, LED count, and baud rate.
