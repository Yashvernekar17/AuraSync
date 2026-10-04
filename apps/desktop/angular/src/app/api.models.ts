/** Profile preferences shared by the renderer and the local service API. */
export interface ProfileSettings {
  displayId: string | null;
  captureMode: 'screen' | 'audio' | 'custom';
  audioDeviceId: string | null;
  soundMode: 'off' | 'average';
  audioColors: string[];
  customEffect: CustomEffect;
  customColors: string[];
  customSpeed: number;
  ledDevice: LedDeviceSettings;
}

/** Supported user-selectable RGB animations handled by the local service. */
export type CustomEffect =
  | 'static'
  | 'blink'
  | 'fade'
  | 'rainbow'
  | 'fire'
  | 'color-cycle'
  | 'color-wipe'
  | 'theater-chase'
  | 'breathing';

/** An active Windows playback endpoint exposed by the local audio provider. */
export interface AudioDevice {
  id: string;
  name: string;
  direction: string;
}

/** Controller connection details and the physical LED layout around a display. */
export interface LedDeviceSettings {
  controllerType: 'adalight' | 'ardulight';
  serialPort: string | null;
  baudRate: number;
  ledCount: number;
  topLeds: number;
  sideLeds: number;
  bottomLeds: number;
  topMarginPercent: number;
  sideMarginPercent: number;
  bottomMarginPercent: number;
  cardSizePercent: number;
  bottomGapPercent: number;
  numberingOffset: number;
  skipCorners: boolean;
  invertOrder: boolean;
}

/** A saved set of sync preferences; at most one profile is active at a time. */
export interface Profile {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  settings: ProfileSettings;
  isActive: boolean;
}

/** Display geometry reported by the local service in virtual-desktop coordinates. */
export interface DisplaySource {
  id: string;
  name: string;
  x: number;
  y: number;
  width: number;
  height: number;
  isPrimary: boolean;
}

/** Current sync runtime state returned by the versioned API. */
export interface SyncStatus {
  isRunning: boolean;
  state: string;
  activeProfileId: string | null;
  selectedDisplayId: string | null;
  framesPerSecond: number;
  message: string | null;
  ledDevice: LedDeviceSettings;
  captureMode: 'screen' | 'audio' | 'custom';
}

/** Runtime settings sent when starting sync or updating its configuration. */
export interface SyncConfigurationRequest extends ProfileSettings {
  framesPerSecond: number;
}
