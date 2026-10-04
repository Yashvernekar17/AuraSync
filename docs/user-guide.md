# AuraSync user guide

This guide walks through connecting a WS2812 LED strip to an Arduino Nano, uploading the controller sketch, configuring AuraSync, and starting screen synchronization.

## App page tour

The screenshots below show the six main pages in AuraSync. They were captured from the running app. Profile details and device status depend on your setup. The browser preview shown here cannot access live desktop capture; use the Electron desktop app for that feature.

### Dashboard

See the active profile, selected display, target or output frame rate, and synchronization status. Start or stop synchronization, check the display preview, and confirm the local service connection.

![AuraSync Dashboard page](images/aurasync-dashboard.png)

### Profiles

Create, activate, and edit saved setups. Choose a display and lighting mode, set controller and COM port options, and adjust the LED count, perimeter layout, and numbering.

![AuraSync Profiles page](images/aurasync-profiles.png)

### Screen source

Select a connected display, check its dimensions and preview status, retry preview startup, and detect available displays.

![AuraSync Screen source page](images/aurasync-screen-source.png)

### Sound to RGB

Review the sound-reactive lighting controls and save sound settings. The audio device and palette controls depend on the selected capture mode.

![AuraSync Sound to RGB page](images/aurasync-sound-to-rgb.png)

### LED output

Check serial output status and adjust local color processing, such as brightness, gamma, smoothing, black cutoff, color averaging, and color temperature.

![AuraSync LED output page](images/aurasync-led-output.png)

### Settings

Choose the target frame rate and application lighting mode, then apply the settings to the running service.

![AuraSync Settings page](images/aurasync-settings.png)

## 1. What you need

- A Windows 10/11 computer running AuraSync.
- An Arduino Nano with a USB data cable.
- A 5 V WS2812/WS2812B addressable LED strip. This example uses **108 LEDs**.
- A regulated **5 V external power supply** sized for your strip, hookup wire, and preferably a 330–470 Ω resistor and a 1000 µF (or larger) capacitor.

### Power and wiring safety

Do **not** power the 108-LED strip from the Nano's 5 V pin or from the computer's USB port. At the common worst-case estimate of 60 mA per LED at full white, 108 LEDs can draw about **6.5 A at 5 V**. Use a suitable regulated supply with headroom (for example, around 8 A or more for this worst-case estimate), and use appropriately sized wire and a fuse. Actual draw depends on strip design and brightness.

Keep the supply's mains wiring enclosed and use a certified supply. Turn power off while wiring. Never connect the external supply's +5 V to the Nano's 5 V pin while the Nano is USB-powered; this can back-feed the computer or damage equipment.

## 2. Wire the Nano and strip

Connect the data input end of the strip (marked **DIN** or with an arrow pointing away from the input) as follows:

| WS2812 strip | Connect to |
|---|---|
| `+5V` | External regulated 5 V supply positive |
| `GND` | External supply negative and Arduino Nano `GND` |
| `DIN` | Nano digital pin `D6`, through a 330–470 Ω series resistor |

Place a 1000 µF or larger capacitor across `+5V` and `GND` near the strip's power input. Observe capacitor polarity. The Nano and LED supply **must share ground** so the data signal has a reference. A classic 5 V Nano's D6 output is suitable for a 5 V WS2812 data input. If using a 3.3 V controller instead, add a suitable 5 V logic-level shifter.

For a long strip or visible color/brightness drop, distribute power to additional points on the strip (power injection); do not rely on thin strip copper to carry the full current over a long distance. Keep power injection polarity correct and use a suitably fused supply lead.

```text
Computer USB ───────── Arduino Nano
                             D6 ── 330–470 Ω ──> DIN [WS2812 strip]
                             GND ──────────────── GND
External 5 V supply ──────── +5V ─────────────── +5V
                     └────── GND ─────────────── GND
```

The diagram shows the shared ground and separate strip supply. Do not join the external `+5V` to Nano `5V` while USB is connected.

## 3. Upload the Arduino sketch

1. Install the **FastLED** library using Arduino IDE's Library Manager.
2. Open a new sketch, paste the example below, and set the Arduino IDE board/processor for your Nano. Some Nano clones need **ATmega328P (Old Bootloader)**.
3. Connect the Nano by USB, choose its COM port under **Tools → Port**, and upload.
4. After upload, close Arduino IDE Serial Monitor/Plotter. AuraSync needs exclusive access to the controller's COM port.

The example uses the Adalight serial frame format: 115200 baud, data on D6, and a fixed 108-LED frame. Keep these values aligned with the AuraSync profile. It includes a short startup chase and checks the frame length and checksum before updating the strip.

```cpp
#include <FastLED.h>

// ============================================================
// Configuration
// ============================================================

constexpr uint16_t LED_COUNT = 108;
constexpr uint8_t DATA_PIN = 6;
constexpr uint32_t SERIAL_BAUD = 115200;

// Maximum brightness: 0-255.
// Lower this if your power supply cannot handle full brightness.
constexpr uint8_t MAX_BRIGHTNESS = 200;

// Serial read timeout in milliseconds.
constexpr uint16_t SERIAL_TIMEOUT_MS = 100;

// Adalight protocol magic word.
constexpr uint8_t MAGIC_A = 'A';
constexpr uint8_t MAGIC_D = 'd';
constexpr uint8_t MAGIC_A2 = 'a';

CRGB leds[LED_COUNT];


// ============================================================
// Utility
// ============================================================

void clearLeds() {
  fill_solid(leds, LED_COUNT, CRGB::Black);
  FastLED.show();
}


// ============================================================
// Startup Animation
// ============================================================

void runStartupAnimation() {
  constexpr CRGB colors[] = {
    CRGB(84, 200, 217),
    CRGB(141, 124, 255),
    CRGB::White,
    CRGB(255, 64, 50),
    CRGB(255, 209, 0)
  };

  constexpr uint8_t colorCount =
      sizeof(colors) / sizeof(colors[0]);

  // Moving gradient
  for (uint16_t frame = 0; frame < 100; ++frame) {

    const uint8_t glow = beatsin8(30, 80, 220);

    for (uint16_t i = 0; i < LED_COUNT; ++i) {

      // 0-255 position around the strip.
      const uint16_t phase =
          (static_cast<uint32_t>(i) * 256UL / LED_COUNT +
           static_cast<uint16_t>(frame) * 3) & 0xFF;

      // Map phase into the color array.
      const uint8_t segment =
          (static_cast<uint16_t>(phase) * (colorCount - 1)) >> 8;

      const uint8_t blendAmount =
          ((static_cast<uint16_t>(phase) * (colorCount - 1)) & 0xFF);

      CRGB color;

      if (segment >= colorCount - 1) {
        color = colors[colorCount - 1];
      } else {
        color = blend(
            colors[segment],
            colors[segment + 1],
            blendAmount
        );
      }

      color.nscale8_video(glow);
      leds[i] = color;
    }

    FastLED.show();
    delay(20);
  }

  // Fade out.
  for (uint8_t i = 0; i < 12; ++i) {
    fadeToBlackBy(leds, LED_COUNT, 40);
    FastLED.show();
    delay(20);
  }

  clearLeds();
}


// ============================================================
// Serial Input
// ============================================================

bool readByteWithTimeout(uint8_t &value) {
  const uint32_t start = millis();

  while (Serial.available() == 0) {
    if (millis() - start >= SERIAL_TIMEOUT_MS) {
      return false;
    }

    // Don't completely hog the CPU.
    yield();
  }

  const int received = Serial.read();

  if (received < 0) {
    return false;
  }

  value = static_cast<uint8_t>(received);
  return true;
}


// ============================================================
// Find "Ada"
// ============================================================

bool findMagicWord() {
  uint8_t state = 0;

  while (true) {
    uint8_t byte;

    if (!readByteWithTimeout(byte)) {
      return false;
    }

    switch (state) {

      case 0:
        if (byte == MAGIC_A) {
          state = 1;
        }
        break;

      case 1:
        if (byte == MAGIC_D) {
          state = 2;
        } else if (byte == MAGIC_A) {
          state = 1;
        } else {
          state = 0;
        }
        break;

      case 2:
        if (byte == MAGIC_A2) {
          return true;
        }

        if (byte == MAGIC_A) {
          state = 1;
        } else {
          state = 0;
        }
        break;
    }
  }
}


// ============================================================
// Receive One Adalight Frame
// ============================================================

bool receiveFrame() {

  // Find "Ada".
  if (!findMagicWord()) {
    return false;
  }

  uint8_t highByte;
  uint8_t lowByte;
  uint8_t checksum;

  if (!readByteWithTimeout(highByte)) {
    return false;
  }

  if (!readByteWithTimeout(lowByte)) {
    return false;
  }

  if (!readByteWithTimeout(checksum)) {
    return false;
  }

  // Adalight encodes LED count as count - 1.
  const uint16_t encodedCount =
      (static_cast<uint16_t>(highByte) << 8) | lowByte;

  const uint16_t ledCount = encodedCount + 1;

  // Validate checksum.
  const uint8_t expectedChecksum =
      highByte ^ lowByte ^ 0x55;

  if (checksum != expectedChecksum) {
    return false;
  }

  // Validate LED count.
  if (ledCount != LED_COUNT) {
    return false;
  }

  // Receive RGB data.
  for (uint16_t i = 0; i < LED_COUNT; ++i) {

    uint8_t r;
    uint8_t g;
    uint8_t b;

    if (!readByteWithTimeout(r)) {
      return false;
    }

    if (!readByteWithTimeout(g)) {
      return false;
    }

    if (!readByteWithTimeout(b)) {
      return false;
    }

    leds[i] = CRGB(r, g, b);
  }

  FastLED.show();

  return true;
}


// ============================================================
// Setup
// ============================================================

void setup() {

  // Start serial first.
  Serial.begin(SERIAL_BAUD);

  // Configure LEDs.
  FastLED.addLeds<NEOPIXEL, DATA_PIN>(leds, LED_COUNT);

  FastLED.setBrightness(MAX_BRIGHTNESS);

  // Start with LEDs off.
  clearLeds();

  // Startup animation.
  runStartupAnimation();

  // Tell host that the controller is ready.
  Serial.print("Ada\n");
}


// ============================================================
// Main Loop
// ============================================================

void loop() {

  // Try to receive a complete frame.
  receiveFrame();
}
```

This example waits for the `Ada` marker, then accepts only a checksummed frame for the configured LED count. If you change `LED_COUNT`, also update the profile's LED count and layout in AuraSync. The profile baud rate must match `SERIAL_BAUD`.

> Earlier revisions of this guide contained an Arduino sample matching a third-party Adalight sketch. The current example has been independently rewritten, but prior Git history remains subject to source/license verification; see [third-party notices](../THIRD-PARTY-NOTICES.md) before distributing repository history.

## 4. Create an AuraSync profile

1. Start the AuraSync desktop app and open **Profiles**.
2. Choose **Create new profile**. Give it a name, select the Windows display, and choose **Screen capture** as the lighting mode.
3. Under **Device connection**, choose **Adalight**, select the Nano's COM port, and set **115200 baud**.
4. Set **Total LEDs** to `108`. For a perimeter with 45 LEDs along the top, 9 on **each** side, and 45 along the bottom, set:
   - Top LEDs: `45`
   - LEDs on each side: `9`
   - Bottom LEDs: `45`

   The total is `45 + (2 × 9) + 45 = 108`. If your physical strip is arranged differently, adjust the edge counts and layout controls to match it. Use the preview to check the numbered LED order; choose **Invert LED order** if it runs opposite to your wiring.
5. Save/create the profile. If it is not active, select **Activate** beside it.

## 5. Start synchronization

1. Open **Settings** and choose a target frame rate. Start at 30 FPS for this 108-LED, 115200-baud example.
2. Return to **Dashboard** and select **Start sync**.
3. Confirm the local service is connected and the Dashboard reports the output running. Use **LED output** to adjust brightness, brightness cap, smoothing, gamma, and black cutoff. Start with moderate brightness.
4. To stop, select **Stop sync** before unplugging the controller or changing its profile's port, baud rate, or LED layout.

The service limits output frame rate according to serial bandwidth and LED count. Increasing the target FPS in Settings cannot exceed that practical serial limit; if you increase the baud rate, update it in both the Arduino sketch and the AuraSync profile.

## 6. Other modes

- **Custom RGB effects** run without screen or audio capture. Choose a built-in effect, colors, and speed in the profile.
- **Sound to RGB** uses an available Windows audio output device. Select the device and a palette in the profile; availability depends on the audio provider and Windows device configuration.
- **Screen capture** uses the Electron desktop app's secure display-capture bridge.

## Troubleshooting

| Symptom | What to check |
|---|---|
| COM port is missing | Reconnect the USB data cable, check Windows Device Manager and Nano USB-serial drivers, then refresh ports in the profile. |
| Port is busy or sync will not start | Close Arduino Serial Monitor/Plotter and any other app using the COM port. Confirm the selected port is the Nano. |
| LEDs stay dark | Verify external 5 V power, strip polarity, shared Nano/supply ground, D6-to-DIN connection, data direction, and that firmware uploaded successfully. |
| Only part of the strip updates | Set AuraSync's total count and sketch `NUM_LEDS` to the same number; verify every pixel receives power and signal. |
| Wrong colors or order | Confirm the strip is WS2812/WS2812B and starts at DIN. Check profile LED order/invert-order and the strip's channel order. |
| LEDs flicker, reset, or show wrong colors at high brightness | Check supply capacity, grounding, wire gauge, the series resistor, and capacitor. Add power injection where needed and reduce brightness while diagnosing. |
| Screen preview is unavailable in a web browser | Run the packaged desktop app or `npm run dev:desktop`; the browser-only UI does not have the Electron capture bridge. |
| Frame rate is lower than the target | This is expected when serial bandwidth is the limit. Use a supported higher matching baud rate or a lower target FPS. |
