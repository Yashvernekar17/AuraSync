import { LedDeviceSettings } from './api.models';

/** Percentage-based LED card bounds used by sampling and preview rendering. */
export interface LedCardPlacement {
  index: number;
  left: number;
  top: number;
  width: number;
  height: number;
}

/** Builds clockwise perimeter placements, respecting margins, skipped corners, and the bottom gap. */
export function calculateLedCardPlacements(layout: LedDeviceSettings): LedCardPlacement[] {
  const left = layout.sideMarginPercent;
  const right = 100 - layout.sideMarginPercent;
  const top = layout.topMarginPercent;
  const bottom = 100 - layout.bottomMarginPercent;
  const innerWidth = right - left;
  const innerHeight = bottom - top;
  const topThickness = layout.cardSizePercent;
  const sideThickness = layout.cardSizePercent;
  const skip = layout.skipCorners ? 1 : 0;
  const cards: LedCardPlacement[] = [];
  const push = (x: number, y: number, w: number, h: number): void => {
    cards.push({
      index: cards.length,
      left: Math.max(0, Math.min(100, x)),
      top: Math.max(0, Math.min(100, y)),
      width: Math.max(0, Math.min(w, 100 - x)),
      height: Math.max(0, Math.min(h, 100 - y)),
    });
  };

  const topUnit = innerWidth / (layout.topLeds + skip * 2 || 1);
  for (let index = 0; index < layout.topLeds; index++)
    push(left + (index + skip) * topUnit, top, topUnit, topThickness);

  const sideUnit = innerHeight / (layout.sideLeds + skip * 2 || 1);
  for (let index = 0; index < layout.sideLeds; index++)
    push(right - sideThickness, top + (index + skip) * sideUnit, sideThickness, sideUnit);

  const bottomCount = layout.bottomLeds;
  const gap = innerWidth * layout.bottomGapPercent / 100;
  const bottomUnit = Math.max(0, innerWidth - gap) / (bottomCount + skip * 2 || 1);
  const leftCount = Math.floor(bottomCount / 2);
  const rightCount = bottomCount - leftCount;
  for (let index = 0; index < rightCount; index++) {
    const x = right - (skip + index + 1) * bottomUnit;
    push(x, bottom - topThickness, bottomUnit, topThickness);
  }
  for (let index = 0; index < leftCount; index++) {
    const x = left + skip * bottomUnit + (leftCount - index - 1) * bottomUnit;
    push(x, bottom - topThickness, bottomUnit, topThickness);
  }

  for (let index = 0; index < layout.sideLeds; index++)
    push(left, bottom - (index + skip + 1) * sideUnit, sideThickness, sideUnit);

  return cards;
}

/** Maps a physical perimeter position through the configured offset and direction. */
export function mapLedIndex(index: number, layout: LedDeviceSettings): number {
  const offset = ((layout.numberingOffset % layout.ledCount) + layout.ledCount) % layout.ledCount;
  const shifted = (index + offset) % layout.ledCount;
  return layout.invertOrder ? layout.ledCount - shifted - 1 : shifted;
}

/** Draws mapped RGB values and one-based LED labels into a connected preview canvas. */
export function drawLedCardCanvas(
  canvas: HTMLCanvasElement | undefined,
  layout: LedDeviceSettings,
  placements: LedCardPlacement[],
  rgb: Uint8Array<ArrayBuffer>,
): void {
  if (!canvas?.isConnected) return;
  const bounds = canvas.getBoundingClientRect();
  if (!bounds.width || !bounds.height) return;
  const scale = window.devicePixelRatio || 1;
  const width = Math.round(bounds.width * scale);
  const height = Math.round(bounds.height * scale);
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  const context = canvas.getContext('2d');
  if (!context) return;
  context.clearRect(0, 0, width, height);
  for (const placement of placements) {
    const ledIndex = mapLedIndex(placement.index, layout);
    const x = placement.left / 100 * width;
    const y = placement.top / 100 * height;
    const w = Math.max(1, placement.width / 100 * width);
    const h = Math.max(1, placement.height / 100 * height);
    if (ledIndex * 3 + 2 >= rgb.length) continue;
    const red = rgb[ledIndex * 3];
    const green = rgb[ledIndex * 3 + 1];
    const blue = rgb[ledIndex * 3 + 2];
    context.fillStyle = `rgb(${red} ${green} ${blue})`;
    context.fillRect(x, y, w, h);
    context.strokeStyle = 'rgba(255,255,255,.72)';
    context.lineWidth = Math.max(1, scale);
    context.strokeRect(x, y, w, h);
    context.fillStyle = 'rgba(255,255,255,.95)';
    context.font = `600 ${Math.max(7, Math.min(10, 8 * scale))}px "DM Mono", monospace`;
    context.textAlign = 'center';
    context.textBaseline = 'middle';
    context.shadowColor = 'rgba(0,0,0,.8)';
    context.shadowBlur = 2 * scale;
    context.fillText(String(ledIndex + 1), x + w / 2, y + h / 2, Math.max(0, w - 2 * scale));
    context.shadowBlur = 0;
  }
}
