import { CustomEffect, LedDeviceSettings } from './api.models';
import { mapLedIndex } from './led-layout';

interface CustomEffectFrameOptions {
  now: number;
  effect: CustomEffect;
  colors: string[];
  speed: number;
  layout: LedDeviceSettings;
  getFrameBuffer: (length: number) => Uint8Array<ArrayBuffer>;
}

/** Generates animated RGB frames and keeps the state needed by the fire effect. */
export class CustomEffectAnimator {
  private startedAt = 0;
  private fireHeat = new Uint8Array(0);
  private lastEffect: CustomEffect | null = null;

  start(now: number): void {
    this.startedAt = now;
  }

  reset(): void {
    this.startedAt = 0;
    this.fireHeat = new Uint8Array(0);
    this.lastEffect = null;
  }

  createFrame({
    now,
    effect,
    colors: colorValues,
    speed,
    layout,
    getFrameBuffer,
  }: CustomEffectFrameOptions): Uint8Array<ArrayBuffer> {
    const frame = getFrameBuffer(layout.ledCount * 3);
    const colors = colorValues.map((color) => this.parseRgbColor(color));
    const duration = Math.max(300, 8000 - speed * 75);
    const blinkInterval = Math.max(100, duration / 6);
    const elapsed = Math.max(0, now - this.startedAt);
    const phase = (elapsed % duration) / duration;
    if (effect === 'fire') {
      if (this.lastEffect !== 'fire' || this.fireHeat.length !== layout.ledCount)
        this.fireHeat = new Uint8Array(layout.ledCount);
      this.updateFireHeat(speed);
    }
    this.lastEffect = effect;

    for (let led = 0; led < layout.ledCount; led++) {
      let color: [number, number, number] = [0, 0, 0];
      switch (effect) {
        case 'static':
          color = colors[Math.min(colors.length - 1, Math.floor(led / layout.ledCount * colors.length))];
          break;
        case 'blink':
          color = Math.floor(elapsed / blinkInterval) % 2 === 0
            ? colors[Math.floor(elapsed / (blinkInterval * 2)) % colors.length]
            : [0, 0, 0];
          break;
        case 'fade':
          color = this.blendPalette(colors, phase * colors.length);
          break;
        case 'rainbow':
          color = this.hsvToRgb((led / layout.ledCount + phase) % 1);
          break;
        case 'fire':
          color = this.fireHeatToRgb(this.fireHeat[led], colors);
          break;
        case 'color-cycle':
          color = this.blendPalette(colors, (elapsed / duration) % colors.length);
          break;
        case 'color-wipe':
          color = led < Math.floor(phase * layout.ledCount)
            ? colors[Math.floor(elapsed / duration) % colors.length]
            : [0, 0, 0];
          break;
        case 'theater-chase':
          color = (led + Math.floor(elapsed / Math.max(50, duration / layout.ledCount))) % 3 === 0
            ? colors[Math.floor(elapsed / duration) % colors.length]
            : [0, 0, 0];
          break;
        case 'breathing': {
          const brightness = (1 - Math.cos(phase * Math.PI * 2)) / 2;
          const base = colors[Math.min(colors.length - 1, Math.floor(led / layout.ledCount * colors.length))];
          color = base.map((channel) => Math.round(channel * brightness)) as [number, number, number];
          break;
        }
      }
      const offset = mapLedIndex(led, layout) * 3;
      frame[offset] = color[0];
      frame[offset + 1] = color[1];
      frame[offset + 2] = color[2];
    }

    return frame;
  }

  private updateFireHeat(speed: number): void {
    const ledCount = this.fireHeat.length;
    if (ledCount === 0) return;

    const coolingRange = Math.max(3, Math.round((80 - speed * 0.65) * 10 / ledCount) + 2);
    for (let led = 0; led < ledCount; led++) {
      this.fireHeat[led] = Math.max(0, this.fireHeat[led] - Math.floor(Math.random() * coolingRange));
    }

    for (let led = ledCount - 1; led >= 2; led--) {
      this.fireHeat[led] = Math.floor(
        (this.fireHeat[led - 1] + this.fireHeat[led - 2] * 2) / 3,
      );
    }

    const sparkChance = 0.12 + speed / 500;
    if (Math.random() < sparkChance) {
      const sparkPosition = Math.floor(Math.random() * Math.min(7, ledCount));
      this.fireHeat[sparkPosition] = Math.min(
        255,
        this.fireHeat[sparkPosition] + 160 + Math.floor(Math.random() * 96),
      );
    }
  }

  private fireHeatToRgb(
    temperature: number,
    colors: [number, number, number][],
  ): [number, number, number] {
    if (temperature === 0) return [0, 0, 0];

    const palettePosition = (temperature / 255) * (colors.length - 1);
    const flameColor = this.blendPalette(colors, palettePosition);
    const brightness = 0.25 + (temperature / 255) * 0.75;
    return flameColor.map((channel) => Math.round(channel * brightness)) as [number, number, number];
  }

  private parseRgbColor(color: string): [number, number, number] {
    return [
      Number.parseInt(color.slice(1, 3), 16),
      Number.parseInt(color.slice(3, 5), 16),
      Number.parseInt(color.slice(5, 7), 16),
    ];
  }

  private blendPalette(colors: [number, number, number][], position: number): [number, number, number] {
    const wrappedPosition = position % colors.length;
    const index = Math.floor(wrappedPosition);
    const start = colors[index];
    const end = colors[(index + 1) % colors.length];
    const fraction = wrappedPosition - index;
    return [
      Math.round(start[0] + (end[0] - start[0]) * fraction),
      Math.round(start[1] + (end[1] - start[1]) * fraction),
      Math.round(start[2] + (end[2] - start[2]) * fraction),
    ];
  }

  private hsvToRgb(hue: number): [number, number, number] {
    const scaledHue = hue * 6;
    const chroma = 255;
    const secondary = chroma * (1 - Math.abs(scaledHue % 2 - 1));
    const sector = Math.floor(scaledHue);
    const channels: [number, number, number] = sector === 0 ? [chroma, secondary, 0]
      : sector === 1 ? [secondary, chroma, 0]
        : sector === 2 ? [0, chroma, secondary]
          : sector === 3 ? [0, secondary, chroma]
            : sector === 4 ? [secondary, 0, chroma]
              : [chroma, 0, secondary];
    return channels.map(Math.round) as [number, number, number];
  }
}
