# Integration testing

Integration tests should run against a temporary profile store and a real display/Arduino, or an explicitly selected test adapter. Do not treat an empty display list or a successful no-op `sync/start` as evidence that capture works.

The initial verification checklist is:

- `GET /api/v1/health` responds on loopback.
- Profile CRUD/activation persists across service restarts.
- On Windows, display IDs and bounds reflect connected monitors; selecting an unavailable ID returns `400`.
- On unsupported platforms, display enumeration responds with an explicit capability error.
- `POST /api/v1/sync/start` opens the configured COM port and waits for the Adalight `Ada` greeting before reporting running.
- The `/api/v1/sync/frames` WebSocket accepts binary frames of exactly three RGB bytes per configured LED and rejects malformed frames.
- Starting screen capture produces edge colors in clockwise order; stopping sync sends fade-down frames ending in black before closing the serial port.
- Verify the Arduino sketch's `NUM_LEDS` and serial rate match the saved profile.
