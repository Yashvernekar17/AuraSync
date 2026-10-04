import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, ElementRef, NgZone, OnDestroy, OnInit, ViewChild, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EMPTY, finalize, forkJoin, Subscription, timer } from 'rxjs';
import { catchError, exhaustMap, map, switchMap } from 'rxjs/operators';
import {
  AudioDevice,
  CustomEffect,
  DisplaySource,
  LedDeviceSettings,
  Profile,
  ProfileSettings,
  SyncConfigurationRequest,
  SyncStatus,
} from './api.models';
import { CustomEffectAnimator } from './custom-effect-animator';
import {
  calculateLedCardPlacements,
  drawLedCardCanvas,
  LedCardPlacement,
  mapLedIndex,
} from './led-layout';
import { LedSyncApiService } from './led-sync-api.service';

type Section = 'dashboard' | 'profiles' | 'screen' | 'sound' | 'leds' | 'settings' | 'legal';

interface ElectronDesktopTrackConstraints extends MediaTrackConstraints {
  mandatory: {
    chromeMediaSource: 'desktop';
    chromeMediaSourceId: string;
    maxWidth: number;
    maxHeight: number;
  };
}

interface LedOutputSettings {
  gamma: number;
  brightness: number;
  brightnessCap: number;
  overBrighten: number;
  smoothing: number;
  colorTemperatureEnabled: boolean;
  colorTemperature: number;
  averageColors: boolean;
  blackLevelThreshold: number;
}

/**
 * Coordinates screen/profile/settings UI state with the local service and Electron capture bridge.
 * Dedicated helpers own custom-effect generation and LED perimeter geometry; frame data stays in the renderer.
 */
@Component({
  selector: 'led-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
})
export class AppComponent implements OnInit, OnDestroy {
  private static readonly selectedProfileStorageKey = 'aurasync.selectedProfileId';
  private static readonly selectedDisplayStorageKey = 'aurasync.selectedDisplayId';
  private static readonly frameRateStorageKey = 'aurasync.framesPerSecond';
  private static readonly ledOutputSettingsStorageKey = 'aurasync.ledOutputSettings';
  private static readonly defaultLedOutputSettings: LedOutputSettings = {
    gamma: 2,
    brightness: 100,
    brightnessCap: 100,
    overBrighten: 0,
    smoothing: 0,
    colorTemperatureEnabled: false,
    colorTemperature: 6500,
    averageColors: false,
    blackLevelThreshold: 0,
  };
  private static readonly defaultLedDeviceSettings: LedDeviceSettings = {
    controllerType: 'adalight',
    serialPort: null,
    baudRate: 115200,
    ledCount: 108,
    topLeds: 45,
    sideLeds: 9,
    bottomLeds: 45,
    topMarginPercent: 0,
    sideMarginPercent: 0,
    bottomMarginPercent: 0,
    cardSizePercent: 15,
    bottomGapPercent: 0,
    numberingOffset: 0,
    skipCorners: false,
    invertOrder: false,
  };
  readonly audioThemes = [
    { name: 'Aurora', colors: ['#5B3FD6', '#8D7CFF', '#5B9BFF', '#54C8D9', '#56D39A', '#B7F06A'] },
    { name: 'Ember', colors: ['#6E1028', '#C51F32', '#FF3B30', '#FF6A24', '#FF9500', '#FFC247', '#FFE99A'] },
    { name: 'Ocean', colors: ['#082C63', '#0759A5', '#0072FF', '#00A8E8', '#00C6FF', '#44E0D0', '#B0F4DF'] },
    { name: 'Spectrum', colors: ['#FF3864', '#FF7043', '#FFB800', '#D9E650', '#56E39F', '#00C6FF', '#3D9BFF', '#B36BFF'] },
    { name: 'Neon', colors: ['#FF00C8', '#FF3478', '#FF6B35', '#DFFF00', '#00F5A0', '#00D9FF', '#4361FF', '#9D4DFF'] },
    { name: 'Sunset', colors: ['#3B1D5A', '#713B78', '#B84D72', '#F06A59', '#FF945C', '#FFC078', '#FFE0A3'] },
    { name: 'Forest', colors: ['#173B32', '#22664B', '#388C55', '#69AD50', '#A8CF59', '#D7DB78'] },
    { name: 'Arctic', colors: ['#194A73', '#287FA5', '#39B7C5', '#77D6D0', '#B5EFE0', '#E7FFF2'] },
    { name: 'Candy', colors: ['#791B73', '#C13AA5', '#F16DB5', '#FF9FC7', '#FFC5B8', '#FFD98A', '#A9E6D2'] },
    { name: 'Vaporwave', colors: ['#321B78', '#673AB7', '#B24AC8', '#F064B4', '#FF8FA3', '#53D9D1', '#55A9FF'] },
    { name: 'Golden Hour', colors: ['#6B2A24', '#A84428', '#D66A2D', '#F39A38', '#FFC45C', '#FFE29A'] },
    { name: 'Ice & Fire', colors: ['#1E3A8A', '#2563EB', '#38BDF8', '#C7F0FF', '#FFE0B2', '#FF9A62', '#EF4444'] },
    { name: 'Pastel Dream', colors: ['#F4A7C5', '#F7C6A3', '#F8E5A6', '#B9E5C9', '#A8D8EA', '#C5B9F2', '#E7B8DF'] },
  ];
  private readonly api = inject(LedSyncApiService);
  private readonly zone = inject(NgZone);
  private readonly changeDetector = inject(ChangeDetectorRef);
  @ViewChild('captureVideo') private captureVideo?: ElementRef<HTMLVideoElement>;
  @ViewChild('dashboardLedCanvas') private dashboardLedCanvas?: ElementRef<HTMLCanvasElement>;
  @ViewChild('profileLedCanvas') private profileLedCanvas?: ElementRef<HTMLCanvasElement>;
  readonly baudRates = [
    2_000_000, 1_500_000, 1_000_000, 921_600, 500_000, 460_800, 256_000,
    230_400, 153_600, 128_000, 115_200, 57_600, 9_600,
  ];
  readonly sections: { id: Section; label: string; icon: string }[] = [
    { id: 'dashboard', label: 'Dashboard', icon: '⌂' },
    { id: 'profiles', label: 'Profiles', icon: '◷' },
    { id: 'screen', label: 'Screen source', icon: '▣' },
    { id: 'sound', label: 'Sound to RGB', icon: '♫' },
    { id: 'leds', label: 'LED output', icon: '✳' },
    { id: 'settings', label: 'Settings', icon: '⚙' },
    { id: 'legal', label: 'Legal notice', icon: '§' },
  ];

  section: Section = 'dashboard';
  profiles: Profile[] = [];
  displays: DisplaySource[] = [];
  audioDevices: AudioDevice[] = [];
  audioDevicesLoading = false;
  audioDevicesUnavailable = false;
  status: SyncStatus | null = null;
  startupLoading = true;
  profilesLoading = false;
  displaysLoading = false;
  serialPortsLoading = false;
  syncActionLoading = false;
  displayChangeLoading = false;
  profileSaving = false;
  isCreatingProfile = false;
  profileActionId: string | null = null;
  profileActionType: 'activate' | 'delete' | null = null;
  runtimeSettingsSaving = false;
  ledOutputSettings: LedOutputSettings = { ...AppComponent.defaultLedOutputSettings };
  profileName = '';
  selectedProfileId: string | null = null;
  selectedDisplayId = '';
  framesPerSecond = 30;
  captureMode: 'screen' | 'audio' | 'custom' = 'screen';
  soundMode: 'off' | 'average' = 'off';
  selectedAudioDeviceId: string | null = null;
  audioColors = [...this.audioThemes[0].colors];
  customEffect: CustomEffect = 'static';
  customColors = ['#8D7CFF'];
  customSpeed = 50;
  readonly customEffects: { value: CustomEffect; label: string }[] = [
    { value: 'static', label: 'Static color' },
    { value: 'blink', label: 'Blink' },
    { value: 'fade', label: 'Fade' },
    { value: 'rainbow', label: 'Rainbow' },
    { value: 'fire', label: 'Fire (flames)' },
    { value: 'color-cycle', label: 'Color cycle' },
    { value: 'color-wipe', label: 'Color wipe' },
    { value: 'theater-chase', label: 'Theater chase' },
    { value: 'breathing', label: 'Breathing' },
  ];
  serialPorts: string[] = [];
  serialPortsUnavailable = false;
  ledDevice: LedDeviceSettings = { ...AppComponent.defaultLedDeviceSettings };
  private errorMessage = '';
  private noticeMessage = '';
  private errorToastTimer?: ReturnType<typeof setTimeout>;
  private noticeToastTimer?: ReturnType<typeof setTimeout>;
  displaysUnavailable = false;
  previewStream: MediaStream | null = null;
  previewLoading = false;
  previewMessage = 'Select a display to start its live preview.';

  private previewDisplayId: string | null = null;
  private previewRequestDisplayId: string | null = null;
  private previewAttemptedDisplayId: string | null = null;
  private previewRequestId = 0;
  private displaySelectionVersion = 0;
  private pendingDisplaySelectionVersion: number | null = null;
  private frameSocket: WebSocket | null = null;
  private frameTimer: ReturnType<typeof setInterval> | null = null;
  private frameTimerRate = 0;
  private frameCanvas: HTMLCanvasElement | null = null;
  private frameContext: CanvasRenderingContext2D | null = null;
  private rgbFrameBuffer = new Uint8Array(0);
  private frameLedDevice: LedDeviceSettings | null = null;
  private readonly customEffectAnimator = new CustomEffectAnimator();
  private blackLedMask = new Uint8Array(0);
  private previousOutputFrame: Uint8Array<ArrayBuffer> | null = null;
  private cachedTemperature = Number.NaN;
  private cachedTemperatureGains: [number, number, number] = [1, 1, 1];
  private readonly neutralTemperatureGains: [number, number, number] = [1, 1, 1];
  private gammaBrightnessLookup = new Float64Array(3 * 256);
  private cachedGamma = Number.NaN;
  private cachedBrightnessScale = Number.NaN;
  private cachedLookupGains: [number, number, number] = [Number.NaN, Number.NaN, Number.NaN];
  private statusPolling?: Subscription;
  private captureResolutionRequestId = 0;
  private readonly visibilityChangeHandler = (): void => {
    if (document.hidden) {
      this.statusPolling?.unsubscribe();
      this.statusPolling = undefined;
      if (this.status?.isRunning && this.status.captureMode === 'screen') {
        void this.setCaptureResolution(640, 360);
      } else {
        this.stopPreview();
      }
      return;
    }

    void this.setCaptureResolution(1280, 720);
    this.renderLatestLedFrame();
    if (!this.startupLoading) {
      this.startStatusPolling();
      this.refreshStatus();
    }
  };
  private destroyed = false;
  private initialRequestsRemaining = 3;
  private initialProfileResolved = false;
  private initialDisplaysResolved = false;
  private initialProfileId: string | null = null;
  private initialDisplayId: string | null = null;
  private hasSavedFrameRate = false;
  private profilesLoaded = false;
  private displaysLoaded = false;
  private profileBeforeCreateId: string | null = null;

  get error(): string {
    return this.errorMessage;
  }

  set error(message: string) {
    this.errorMessage = message;
    if (this.errorToastTimer !== undefined) clearTimeout(this.errorToastTimer);
    this.errorToastTimer = message
      ? setTimeout(() => {
        if (this.errorMessage !== message) return;
        this.errorMessage = '';
        this.errorToastTimer = undefined;
        this.markUiForCheck();
      }, 5000)
      : undefined;
    this.markUiForCheck();
  }

  get notice(): string {
    return this.noticeMessage;
  }

  set notice(message: string) {
    this.noticeMessage = message;
    if (this.noticeToastTimer !== undefined) clearTimeout(this.noticeToastTimer);
    this.noticeToastTimer = message
      ? setTimeout(() => {
        if (this.noticeMessage !== message) return;
        this.noticeMessage = '';
        this.noticeToastTimer = undefined;
        this.markUiForCheck();
      }, 5000)
      : undefined;
    this.markUiForCheck();
  }

  ngOnInit(): void {
    document.addEventListener('visibilitychange', this.visibilityChangeHandler);
    this.loadLedOutputSettings();
    this.initialProfileId = localStorage.getItem(AppComponent.selectedProfileStorageKey);
    this.initialDisplayId = localStorage.getItem(AppComponent.selectedDisplayStorageKey);
    const savedFrameRate = Number(localStorage.getItem(AppComponent.frameRateStorageKey));
    this.hasSavedFrameRate = Number.isFinite(savedFrameRate) && savedFrameRate >= 1 && savedFrameRate <= 240;
    if (this.hasSavedFrameRate) this.framesPerSecond = savedFrameRate;
    this.loadSerialPorts();
    this.profilesLoading = true;
    this.api.getProfiles().pipe(finalize(() => {
      this.profilesLoading = false;
      this.profilesLoaded = true;
      this.initialProfileResolved = true;
      this.applyInitialDisplaySelection();
      this.finishInitialRequest();
    })).subscribe({
      next: (profiles) => {
        this.profiles = profiles;
        const selected = profiles.find((profile) => profile.id === this.initialProfileId)
          ?? profiles.find((profile) => profile.isActive)
          ?? profiles[0];
        if (selected) this.applyProfile(selected);
        this.applyInitialDisplaySelection();
      },
      error: (error: unknown) => this.showError(error),
    });

    this.displaysLoading = true;
    this.api.getDisplays().pipe(finalize(() => {
      this.displaysLoading = false;
      this.displaysLoaded = true;
      this.initialDisplaysResolved = true;
      this.applyInitialDisplaySelection();
      this.finishInitialRequest();
    })).subscribe({
      next: (displays) => {
        this.displays = displays;
        this.displaysUnavailable = false;
        this.applyInitialDisplaySelection();
      },
      error: (error: unknown) => {
        this.displaysUnavailable = true;
        this.showError(error);
      },
    });

    this.api.getStatus().pipe(finalize(() => {
      this.finishInitialRequest();
    })).subscribe({
      next: (status) => {
        this.status = status;
        this.frameLedDevice = status.isRunning ? { ...status.ledDevice } : null;
        if (!this.selectedProfileId) {
          this.selectedDisplayId = status.selectedDisplayId ?? '';
        }
        if (!this.hasSavedFrameRate) this.framesPerSecond = status.framesPerSecond;
        this.applyInitialDisplaySelection();
        this.syncFrameTransport();
      },
      error: (error: unknown) => this.showError(error),
    });
    void window.ledSyncDesktop?.notifyUiReady().catch((error: unknown) => this.showError(error));
  }

  private finishInitialRequest(): void {
    this.initialRequestsRemaining--;
    if (this.initialRequestsRemaining === 0) {
      this.startupLoading = false;
      this.startStatusPolling();
    }
    this.markUiForCheck();
  }

  private applyInitialDisplaySelection(): void {
    if (!this.initialProfileResolved || !this.initialDisplaysResolved) return;
    const displayId = this.displays.some((display) => display.id === this.initialDisplayId)
      ? this.initialDisplayId
      : this.selectedDisplayId;
    if (displayId && displayId !== this.selectedDisplayId) {
      this.selectedDisplayId = displayId;
      this.displaySelectionVersion++;
      this.pendingDisplaySelectionVersion = this.displaySelectionVersion;
      localStorage.setItem(AppComponent.selectedDisplayStorageKey, displayId);
    }
    this.syncPreviewToSelection();
  }

  private startStatusPolling(): void {
    if (this.destroyed || document.hidden || this.statusPolling) return;
    this.statusPolling = timer(2000, 2000)
      .pipe(exhaustMap(() => {
        const selectionVersion = this.displaySelectionVersion;
        return this.api.getStatus().pipe(
          map((status) => ({ status, selectionVersion })),
          catchError((error: unknown) => {
            this.status = null;
            this.showError(error);
            return EMPTY;
          }),
        );
      }))
      .subscribe({
        next: ({ status, selectionVersion }) => this.applyStatus(status, selectionVersion),
      });
  }

  private refreshStatus(): void {
    const selectionVersion = this.displaySelectionVersion;
    this.api.getStatus().subscribe({
      next: (status) => {
        this.applyStatus(status, selectionVersion);
        this.renderLatestLedFrame();
      },
      error: (error: unknown) => {
        this.status = null;
        this.showError(error);
      },
    });
  }

  private async setCaptureResolution(width: number, height: number): Promise<void> {
    const track = this.previewStream?.getVideoTracks()[0];
    if (!track) return;

    const requestId = ++this.captureResolutionRequestId;
    try {
      await track.applyConstraints({
        width: { ideal: width, max: width },
        height: { ideal: height, max: height },
      });
    } catch (error: unknown) {
      if (requestId === this.captureResolutionRequestId) {
        this.zone.run(() => this.showError(error));
      }
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    document.removeEventListener('visibilitychange', this.visibilityChangeHandler);
    if (this.errorToastTimer !== undefined) clearTimeout(this.errorToastTimer);
    if (this.noticeToastTimer !== undefined) clearTimeout(this.noticeToastTimer);
    this.statusPolling?.unsubscribe();
    this.stopPreview();
    this.stopFrameTransport();
  }

  get activeProfile(): Profile | undefined {
    return this.profiles.find((profile) => profile.isActive);
  }

  get selectedDisplay(): DisplaySource | undefined {
    return this.displays.find((display) => display.id === this.selectedDisplayId);
  }

  get selectedDisplayAspectRatio(): string {
    const display = this.selectedDisplay;
    return display && display.width > 0 && display.height > 0
      ? `${display.width} / ${display.height}`
      : '16 / 9';
  }

  get savedAudioDeviceMissing(): boolean {
    return !!this.selectedAudioDeviceId &&
      !this.audioDevices.some((device) => device.id === this.selectedAudioDeviceId);
  }

  get displaySummary(): string {
    const display = this.selectedDisplay;
    return display
      ? `${display.id} · ${display.width}×${display.height}`
      : `${this.displays.length} display${this.displays.length === 1 ? '' : 's'} detected`;
  }

  navigate(section: Section): void {
    this.section = section;
    this.clearMessages();
    if (section === 'screen' && !this.displaysLoaded && !this.displaysLoading) {
      this.loadDisplays();
    } else if (section === 'screen') {
      this.syncPreviewToSelection();
    }
    if (section === 'profiles' && !this.profilesLoaded && !this.profilesLoading) {
      this.loadProfiles();
    }
    if (section === 'sound') this.loadAudioDevices();
  }

  retryPreview(): void {
    this.previewAttemptedDisplayId = null;
    this.syncPreviewToSelection();
  }

  get maxLedCount(): number {
    return 255;
  }

  get effectiveOutputFrameRate(): number {
    return this.effectiveFrameRate();
  }

  get ledCardPlacements(): LedCardPlacement[] {
    return calculateLedCardPlacements(this.ledDevice);
  }

  ledCardNumber(index: number): number {
    return mapLedIndex(index, this.ledDevice) + 1;
  }

  get previewLedDevice(): LedDeviceSettings {
    return this.status?.isRunning
      ? this.status.ledDevice
      : this.activeProfile?.settings.ledDevice ?? this.ledDevice;
  }

  beginCreateProfile(): void {
    if (this.profileSaving) return;
    this.profileBeforeCreateId = this.selectedProfileId;
    this.isCreatingProfile = true;
    this.selectedProfileId = null;
    this.profileName = '';
    this.captureMode = 'screen';
    this.soundMode = 'off';
    this.selectedAudioDeviceId = this.audioDevices[0]?.id ?? null;
    this.audioColors = [...this.audioThemes[0].colors];
    this.customEffect = 'static';
    this.customColors = ['#8D7CFF'];
    this.customSpeed = 50;
    this.ledDevice = { ...AppComponent.defaultLedDeviceSettings };
  }

  cancelCreateProfile(): void {
    const previous = this.profiles.find((profile) => profile.id === this.profileBeforeCreateId)
      ?? this.activeProfile
      ?? this.profiles[0];
    this.profileBeforeCreateId = null;
    this.isCreatingProfile = false;
    if (previous) {
      this.selectProfile(previous);
      return;
    }
    this.profileName = '';
    this.captureMode = 'screen';
    this.soundMode = 'off';
    this.selectedAudioDeviceId = null;
    this.audioColors = [...this.audioThemes[0].colors];
    this.customEffect = 'static';
    this.customColors = ['#8D7CFF'];
    this.customSpeed = 50;
    this.ledDevice = { ...AppComponent.defaultLedDeviceSettings };
  }

  createProfile(): void {
    if (this.profileSaving) return;
    const name = this.profileName.trim();
    if (!name) {
      this.error = 'Enter a profile name.';
      return;
    }
    this.profileSaving = true;
    this.api.createProfile(name, this.currentProfileSettings()).subscribe({
      next: (profile) => {
        this.profiles = [...this.profiles, profile];
        this.selectedProfileId = profile.id;
        this.profileName = profile.name;
        this.isCreatingProfile = false;
        this.profileBeforeCreateId = null;
        this.rememberSelectedProfile(profile.id);
        this.notice = `Profile “${profile.name}” created.`;
        if (profile.isActive) this.applyProfile(profile);
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.profileSaving = false;
      this.markUiForCheck();
    });
  }

  selectProfile(profile: Profile): void {
    this.isCreatingProfile = false;
    this.profileBeforeCreateId = null;
    this.displaySelectionVersion++;
    this.pendingDisplaySelectionVersion = this.displaySelectionVersion;
    this.selectedProfileId = profile.id;
    this.rememberSelectedProfile(profile.id);
    this.profileName = profile.name;
    this.selectedDisplayId = profile.settings.displayId ?? '';
    this.captureMode = profile.settings.captureMode;
    this.selectedAudioDeviceId = profile.settings.audioDeviceId;
    this.soundMode = this.captureMode === 'audio' ? 'average' : 'off';
    this.audioColors = [...(profile.settings.audioColors?.length
      ? profile.settings.audioColors
      : this.audioThemes[0].colors)];
    this.customEffect = profile.settings.customEffect ?? 'static';
    this.customColors = [...(profile.settings.customColors?.length
      ? profile.settings.customColors
      : ['#8D7CFF'])];
    this.customSpeed = this.clampSetting(profile.settings.customSpeed, 1, 100, 50);
    this.ledDevice = { ...profile.settings.ledDevice };
    if (this.captureMode === 'audio') this.loadAudioDevices();
    this.syncPreviewToSelection();
  }

  changeCaptureMode(mode: 'screen' | 'audio' | 'custom'): void {
    this.captureMode = mode;
    if (mode === 'audio') {
      this.soundMode = 'average';
      this.loadAudioDevices();
    } else {
      this.soundMode = 'off';
    }
  }

  loadAudioDevices(force = false): void {
    if (this.audioDevicesLoading || (this.audioDevices.length > 0 && !force)) return;
    this.audioDevicesLoading = true;
    this.api.getAudioDevices().subscribe({
      next: (devices) => {
        this.audioDevices = devices;
        this.audioDevicesUnavailable = false;
        if (!this.selectedAudioDeviceId)
          this.selectedAudioDeviceId = devices[0]?.id ?? null;
      },
      error: (error: unknown) => {
        this.audioDevicesUnavailable = true;
        this.showError(error);
      },
    }).add(() => {
      this.audioDevicesLoading = false;
      this.markUiForCheck();
    });
  }

  setAudioTheme(colors: string[]): void {
    this.audioColors = [...colors];
  }

  setCustomTheme(colors: string[]): void {
    this.customColors = [...colors];
  }

  addCustomColor(): void {
    if (this.customColors.length < 8) this.customColors = [...this.customColors, '#FFFFFF'];
  }

  removeCustomColor(index: number): void {
    if (this.customColors.length > 1)
      this.customColors = this.customColors.filter((_color, colorIndex) => colorIndex !== index);
  }

  isAudioThemeSelected(colors: string[]): boolean {
    return colors.length === this.audioColors.length &&
      colors.every((color, index) => color.toLowerCase() === this.audioColors[index]?.toLowerCase());
  }

  addAudioColor(): void {
    if (this.audioColors.length < 8) this.audioColors = [...this.audioColors, '#FFFFFF'];
  }

  removeAudioColor(index: number): void {
    if (this.audioColors.length > 2)
      this.audioColors = this.audioColors.filter((_color, colorIndex) => colorIndex !== index);
  }

  selectProfileDisplay(displayId: string): void {
    this.displaySelectionVersion++;
    this.pendingDisplaySelectionVersion = this.displaySelectionVersion;
    this.selectedDisplayId = displayId;
    if (displayId) localStorage.setItem(AppComponent.selectedDisplayStorageKey, displayId);
    else localStorage.removeItem(AppComponent.selectedDisplayStorageKey);
    this.syncPreviewToSelection();
  }

  saveProfile(): void {
    const selected = this.profiles.find((profile) => profile.id === this.selectedProfileId);
    if (!selected || this.profileSaving) return;
    if (!this.profileName.trim()) {
      this.error = 'Enter a profile name.';
      return;
    }
    this.profileSaving = true;
    this.api.updateProfile(selected.id, this.profileName, this.currentProfileSettings()).subscribe({
      next: (updated) => {
        this.profiles = this.profiles.map((profile) => profile.id === updated.id ? updated : profile);
        if (updated.isActive) this.applyProfile(updated);
        this.notice = 'Profile settings saved.';
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.profileSaving = false;
      this.markUiForCheck();
    });
  }

  activateProfile(profile: Profile): void {
    if (this.profileActionId) return;
    this.profileActionId = profile.id;
    this.profileActionType = 'activate';
    this.api.activateProfile(profile.id).subscribe({
      next: (status) => {
        this.profiles = this.profiles.map((item) => ({ ...item, isActive: item.id === profile.id }));
        this.applyProfile(profile);
        this.applyStatus(status, this.displaySelectionVersion);
        this.notice = `${profile.name} is now active.`;
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.profileActionId = null;
      this.profileActionType = null;
      this.markUiForCheck();
    });
  }

  loadSerialPorts(): void {
    if (this.serialPortsLoading) return;
    this.serialPortsLoading = true;
    this.api.getSerialPorts().subscribe({
      next: (ports) => {
        this.serialPorts = ports;
        this.serialPortsUnavailable = false;
      },
      error: (error: unknown) => {
        this.serialPortsUnavailable = true;
        this.showError(error);
      },
    }).add(() => {
      this.serialPortsLoading = false;
      this.markUiForCheck();
    });
  }

  changeControllerType(controllerType: 'adalight' | 'ardulight'): void {
    this.ledDevice = { ...this.ledDevice, controllerType };
    this.updateLayoutFromTotal();
  }

  updateLayoutFromTotal(): void {
    const ledCount = Math.min(this.maxLedCount, Math.max(1, Math.trunc(Number(this.ledDevice.ledCount) || 1)));
    const topLeds = Math.min(ledCount, Math.max(0, Math.trunc(Number(this.ledDevice.topLeds) || 0)));
    const sideLeds = Math.min(
      Math.floor((ledCount - topLeds) / 2),
      Math.max(0, Math.trunc(Number(this.ledDevice.sideLeds) || 0)),
    );
    this.ledDevice = {
      ...this.ledDevice,
      ledCount,
      topLeds,
      sideLeds,
      bottomLeds: ledCount - topLeds - (sideLeds * 2),
    };
  }

  updateTotalFromLayout(): void {
    const topLeds = Math.max(0, Math.trunc(Number(this.ledDevice.topLeds) || 0));
    const sideLeds = Math.max(0, Math.trunc(Number(this.ledDevice.sideLeds) || 0));
    const bottomLeds = Math.max(0, Math.trunc(Number(this.ledDevice.bottomLeds) || 0));
    const normalizedTop = Math.min(topLeds, this.maxLedCount);
    const normalizedSide = Math.min(sideLeds, Math.floor((this.maxLedCount - normalizedTop) / 2));
    const normalizedBottom = Math.min(
      bottomLeds,
      this.maxLedCount - normalizedTop - (normalizedSide * 2),
    );
    this.ledDevice = {
      ...this.ledDevice,
      topLeds: normalizedTop,
      sideLeds: normalizedSide,
      bottomLeds: normalizedBottom,
      ledCount: normalizedTop + (normalizedSide * 2) + normalizedBottom,
    };
  }

  deleteProfile(profile: Profile): void {
    if (this.profileActionId) return;
    this.profileActionId = profile.id;
    this.profileActionType = 'delete';
    this.api.deleteProfile(profile.id).subscribe({
      next: () => {
        this.profiles = this.profiles.filter((item) => item.id !== profile.id);
        if (this.selectedProfileId === profile.id) {
          this.selectedProfileId = null;
          this.profileName = '';
          localStorage.removeItem(AppComponent.selectedProfileStorageKey);
        }
        this.notice = `${profile.name} deleted.`;
        this.refreshProfilesAndStatus();
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.profileActionId = null;
      this.profileActionType = null;
      this.markUiForCheck();
    });
  }

  changeDisplay(displayId: string): void {
    if (this.displayChangeLoading) return;
    if (displayId === this.selectedDisplayId && this.previewStream?.active) return;
    const previousDisplayId = this.selectedDisplayId;
    const selectionVersion = ++this.displaySelectionVersion;
    this.pendingDisplaySelectionVersion = selectionVersion;
    this.displayChangeLoading = true;
    this.selectedDisplayId = displayId;
    this.stopPreview();

    this.api.setDisplay(displayId).pipe(finalize(() => {
      this.displayChangeLoading = false;
      this.markUiForCheck();
    })).subscribe({
      next: (status) => {
        if (selectionVersion !== this.displaySelectionVersion) return;
        localStorage.setItem(AppComponent.selectedDisplayStorageKey, displayId);
        this.status = status;
        this.frameLedDevice = status.isRunning ? { ...status.ledDevice } : null;
        if (status.selectedDisplayId === displayId)
          this.pendingDisplaySelectionVersion = null;
        this.saveActiveProfileSettings();
        this.previewAttemptedDisplayId = null;
        this.notice = 'Display source updated. Reconnecting preview…';
        this.syncPreviewToSelection();
        this.syncFrameTransport();
      },
      error: (error: unknown) => {
        if (selectionVersion !== this.displaySelectionVersion) return;
        this.pendingDisplaySelectionVersion = null;
        this.displayChangeLoading = false;
        this.selectedDisplayId = previousDisplayId;
        this.displaySelectionVersion++;
        this.syncPreviewToSelection();
        this.showError(error);
      },
    });
  }

  saveRuntimeSettings(): void {
    if (this.runtimeSettingsSaving) return;
    this.runtimeSettingsSaving = true;
    const selectionVersion = this.displaySelectionVersion;
    this.api.setConfiguration(this.currentSettings()).subscribe({
      next: (status) => {
        this.applyStatus(status, selectionVersion);
        this.saveFrameRatePreference();
        this.saveActiveProfileSettings();
        this.notice = 'Runtime settings updated.';
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.runtimeSettingsSaving = false;
      this.markUiForCheck();
    });
  }

  saveLedOutputSettings(): void {
    try {
      this.ledOutputSettings = {
        gamma: this.clampSetting(this.ledOutputSettings.gamma, 0.05, 10, AppComponent.defaultLedOutputSettings.gamma),
        brightness: this.clampSetting(this.ledOutputSettings.brightness, 0, 100, AppComponent.defaultLedOutputSettings.brightness),
        brightnessCap: this.clampSetting(this.ledOutputSettings.brightnessCap, 1, 100, AppComponent.defaultLedOutputSettings.brightnessCap),
        overBrighten: this.clampSetting(this.ledOutputSettings.overBrighten, 0, 100, AppComponent.defaultLedOutputSettings.overBrighten),
        smoothing: this.clampSetting(this.ledOutputSettings.smoothing, 0, 255, AppComponent.defaultLedOutputSettings.smoothing),
        colorTemperatureEnabled: this.ledOutputSettings.colorTemperatureEnabled,
        colorTemperature: this.clampSetting(this.ledOutputSettings.colorTemperature, 1000, 10000, AppComponent.defaultLedOutputSettings.colorTemperature),
        averageColors: this.ledOutputSettings.averageColors,
        blackLevelThreshold: this.clampSetting(this.ledOutputSettings.blackLevelThreshold, 0, 32, AppComponent.defaultLedOutputSettings.blackLevelThreshold),
      };
      localStorage.setItem(
        AppComponent.ledOutputSettingsStorageKey,
        JSON.stringify(this.ledOutputSettings),
      );
      this.previousOutputFrame = null;
    } catch (error: unknown) {
      this.showError(new Error(`Unable to save LED output settings: ${this.api.errorMessage(error)}`));
    }
  }

  private saveFrameRatePreference(): void {
    try {
      localStorage.setItem(AppComponent.frameRateStorageKey, String(this.framesPerSecond));
      this.hasSavedFrameRate = true;
    } catch (error: unknown) {
      this.showError(new Error(`Unable to save the target frame rate: ${this.api.errorMessage(error)}`));
    }
  }

  resetLedOutputSettings(): void {
    this.ledOutputSettings = { ...AppComponent.defaultLedOutputSettings };
    this.saveLedOutputSettings();
    this.notice = 'LED output settings restored to defaults.';
  }

  toggleSync(): void {
    if (this.syncActionLoading) return;
    this.syncActionLoading = true;
    const selectionVersion = this.displaySelectionVersion;
    const action = this.status?.isRunning
      ? this.api.stop()
      : this.api.start(this.currentSettings());
    action.subscribe({
      next: (status) => {
        this.applyStatus(status, selectionVersion);
        this.notice = status.isRunning ? 'Synchronization started.' : 'Synchronization stopped.';
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.syncActionLoading = false;
      this.markUiForCheck();
    });
  }

  reload(): void {
    this.clearMessages();
    this.loadProfiles();
    this.loadDisplays();
  }

  private loadProfiles(): void {
    if (this.profilesLoading) return;
    this.profilesLoading = true;
    this.api.getProfiles().subscribe({
      next: (profiles) => {
        this.profilesLoaded = true;
        this.profiles = profiles;
        if (!this.selectedProfileId && profiles.length) {
          const rememberedId = localStorage.getItem(AppComponent.selectedProfileStorageKey);
          const selected = profiles.find((profile) => profile.id === rememberedId)
            ?? profiles.find((profile) => profile.isActive)
            ?? profiles[0];
          this.applyProfile(selected);
        }
      },
      error: (error: unknown) => this.showError(error),
    }).add(() => {
      this.profilesLoaded = true;
      this.profilesLoading = false;
      this.markUiForCheck();
    });
  }

  private loadDisplays(): void {
    if (this.displaysLoading) return;
    this.displaysLoading = true;
    this.api.getDisplays().subscribe({
      next: (displays) => {
        this.displaysLoaded = true;
        this.displays = displays;
        this.displaysUnavailable = false;
      },
      error: (error: unknown) => {
        this.displaysUnavailable = true;
        this.showError(error);
      },
    }).add(() => {
      this.displaysLoaded = true;
      this.displaysLoading = false;
      this.markUiForCheck();
    });
  }

  private refreshProfilesAndStatus(): void {
    const selectionVersion = this.displaySelectionVersion;
    forkJoin({ profiles: this.api.getProfiles(), status: this.api.getStatus() }).subscribe({
      next: ({ profiles, status }) => {
        this.profiles = profiles;
        this.applyStatus(status, selectionVersion);
      },
      error: (error: unknown) => this.showError(error),
    });
  }

  private saveActiveProfileSettings(): void {
    const active = this.activeProfile;
    if (!active) return;
    this.api.updateProfile(active.id, active.name, this.currentProfileSettings()).subscribe({
      next: (updated) => {
        this.profiles = this.profiles.map((profile) => profile.id === updated.id ? updated : profile);
        this.markUiForCheck();
      },
      error: (error: unknown) => this.showError(error),
    });
  }

  private loadLedOutputSettings(): void {
    try {
      const stored = localStorage.getItem(AppComponent.ledOutputSettingsStorageKey);
      if (!stored) return;
      const settings = JSON.parse(stored) as Partial<LedOutputSettings>;
      this.ledOutputSettings = {
        gamma: this.clampSetting(settings.gamma, 0.05, 10, AppComponent.defaultLedOutputSettings.gamma),
        brightness: this.clampSetting(settings.brightness, 0, 100, AppComponent.defaultLedOutputSettings.brightness),
        brightnessCap: this.clampSetting(settings.brightnessCap, 1, 100, AppComponent.defaultLedOutputSettings.brightnessCap),
        overBrighten: this.clampSetting(settings.overBrighten, 0, 100, AppComponent.defaultLedOutputSettings.overBrighten),
        smoothing: this.clampSetting(settings.smoothing, 0, 255, AppComponent.defaultLedOutputSettings.smoothing),
        colorTemperatureEnabled: typeof settings.colorTemperatureEnabled === 'boolean'
          ? settings.colorTemperatureEnabled
          : AppComponent.defaultLedOutputSettings.colorTemperatureEnabled,
        colorTemperature: this.clampSetting(settings.colorTemperature, 1000, 10000, AppComponent.defaultLedOutputSettings.colorTemperature),
        averageColors: typeof settings.averageColors === 'boolean'
          ? settings.averageColors
          : AppComponent.defaultLedOutputSettings.averageColors,
        blackLevelThreshold: this.clampSetting(settings.blackLevelThreshold, 0, 32, AppComponent.defaultLedOutputSettings.blackLevelThreshold),
      };
    } catch (error: unknown) {
      this.showError(new Error(`Unable to read saved LED output settings: ${this.api.errorMessage(error)}`));
    }
  }

  private clampSetting(value: unknown, minimum: number, maximum: number, fallback: number): number {
    if (typeof value !== 'number' || !Number.isFinite(value)) return fallback;
    return Math.min(maximum, Math.max(minimum, value));
  }

  private currentSettings(): SyncConfigurationRequest {
    return {
      ...this.currentProfileSettings(),
      framesPerSecond: this.framesPerSecond,
    };
  }

  private currentProfileSettings(): ProfileSettings {
    return {
      displayId: this.selectedDisplayId || null,
      captureMode: this.captureMode,
      audioDeviceId: this.selectedAudioDeviceId,
      soundMode: this.soundMode,
      audioColors: [...this.audioColors],
      customEffect: this.customEffect,
      customColors: [...this.customColors],
      customSpeed: this.customSpeed,
      ledDevice: { ...this.ledDevice, serialPort: this.ledDevice.serialPort?.trim() || null },
    };
  }

  private applyProfile(profile: Profile): void {
    this.isCreatingProfile = false;
    this.profileBeforeCreateId = null;
    this.selectedProfileId = profile.id;
    this.rememberSelectedProfile(profile.id);
    this.profileName = profile.name;
    this.displaySelectionVersion++;
    this.pendingDisplaySelectionVersion = this.displaySelectionVersion;
    this.selectedDisplayId = profile.settings.displayId ?? '';
    if (this.selectedDisplayId)
      localStorage.setItem(AppComponent.selectedDisplayStorageKey, this.selectedDisplayId);
    else
      localStorage.removeItem(AppComponent.selectedDisplayStorageKey);
    this.captureMode = profile.settings.captureMode;
    this.selectedAudioDeviceId = profile.settings.audioDeviceId;
    this.soundMode = this.captureMode === 'audio' ? 'average' : 'off';
    this.audioColors = [...(profile.settings.audioColors?.length
      ? profile.settings.audioColors
      : this.audioThemes[0].colors)];
    this.customEffect = profile.settings.customEffect ?? 'static';
    this.customColors = [...(profile.settings.customColors?.length
      ? profile.settings.customColors
      : ['#8D7CFF'])];
    this.customSpeed = this.clampSetting(profile.settings.customSpeed, 1, 100, 50);
    this.ledDevice = { ...profile.settings.ledDevice };
    if (this.captureMode === 'audio') this.loadAudioDevices();
    this.syncPreviewToSelection();
  }

  private rememberSelectedProfile(profileId: string): void {
    localStorage.setItem(AppComponent.selectedProfileStorageKey, profileId);
  }

  private applyStatus(status: SyncStatus, selectionVersion: number): void {
    const wasRunning = this.status?.isRunning === true;
    this.status = status;
    this.frameLedDevice = status.isRunning ? { ...status.ledDevice } : null;
    if (!status.isRunning) this.previousOutputFrame = null;
    // Ignore a poll started before a newer display choice; stale status must not undo the user's selection.
    if (selectionVersion === this.displaySelectionVersion) {
      if (this.pendingDisplaySelectionVersion === selectionVersion) {
        if (status.selectedDisplayId === this.selectedDisplayId)
          this.pendingDisplaySelectionVersion = null;
      } else {
        this.selectedDisplayId = status.selectedDisplayId ?? '';
      }
    }
    if (wasRunning && !status.isRunning) {
      this.stopPreview();
      this.stopFrameTransport();
    } else {
      this.syncPreviewToSelection();
      this.syncFrameTransport();
    }
    this.markUiForCheck();
  }

  private syncPreviewToSelection(): void {
    const displayId = this.selectedDisplayId;
    if (!displayId) {
      this.stopPreview();
      this.previewMessage = 'Select a display to start its live preview.';
      return;
    }
    if (document.hidden &&
        !(this.status?.isRunning && this.status.captureMode === 'screen')) return;
    if (this.previewDisplayId === displayId && this.previewStream?.active) return;
    if (this.previewLoading && this.previewRequestDisplayId === displayId) return;
    if (this.previewAttemptedDisplayId === displayId) return;

    this.previewAttemptedDisplayId = displayId;
    void this.startPreview(displayId);
  }

  private async startPreview(displayId: string): Promise<void> {
    const requestId = ++this.previewRequestId;
    this.releasePreviewStream();
    this.previewLoading = true;
    this.previewRequestDisplayId = displayId;
    this.previewMessage = 'Finding the selected display…';

    try {
      const desktop = window.ledSyncDesktop;
      if (!desktop) {
        throw new Error('Live screen preview is available in the AuraSync desktop app.');
      }

      // Capture startup can outlive a display change; request IDs invalidate late results and streams.
      const stream = await this.zone.runOutsideAngular(async () => {
        const sourceId = await this.withTimeout(
          desktop.getScreenCaptureSourceId(displayId),
          8000,
          'Timed out while locating the selected display.',
        );
        if (requestId !== this.previewRequestId) return null;

        this.zone.run(() => {
          this.previewMessage = 'Opening the selected display capture…';
          this.markUiForCheck();
        });
        const video: ElectronDesktopTrackConstraints = {
          mandatory: {
            chromeMediaSource: 'desktop',
            chromeMediaSourceId: sourceId,
            maxWidth: 1280,
            maxHeight: 720,
          },
        };
        const captureRequest = navigator.mediaDevices.getUserMedia({ audio: false, video });
        void captureRequest.then((lateStream) => {
          if (requestId !== this.previewRequestId)
            lateStream.getTracks().forEach((track) => track.stop());
        }, () => undefined);
        return this.withTimeout(
          captureRequest,
          10000,
          'Timed out while opening the selected display preview. Retry to reconnect.',
        );
      });
      if (!stream) return;
      if (requestId !== this.previewRequestId) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }

      this.zone.run(() => {
        this.previewStream = stream;
        this.previewDisplayId = displayId;
        this.previewMessage = 'Live preview from the selected display.';
        this.markUiForCheck();
        stream.getVideoTracks().forEach((track) => track.addEventListener('ended', () => {
          this.zone.run(() => {
            if (this.previewStream !== stream) return;
            this.previewStream = null;
            this.previewDisplayId = null;
            this.previewMessage = 'Display capture ended. Retry the preview to reconnect.';
            this.syncFrameTransport();
            this.markUiForCheck();
            if (this.status?.isRunning && this.status.captureMode === 'screen') {
              this.api.stop().subscribe({
                next: (status) => this.applyStatus(status, this.displaySelectionVersion),
                error: (error: unknown) => this.showError(error),
              });
            }
          });
        }));
        setTimeout(() => this.syncFrameTransport(), 0);
      });
    } catch (error: unknown) {
      if (requestId !== this.previewRequestId) return;
      this.zone.run(() => {
        this.previewMessage = error instanceof Error
          ? error.message
          : 'Unable to start the live display preview.';
        this.markUiForCheck();
      });
    } finally {
      if (requestId === this.previewRequestId) {
        this.zone.run(() => {
          this.previewLoading = false;
          this.previewRequestDisplayId = null;
          this.markUiForCheck();
        });
      }
    }
  }

  private withTimeout<T>(promise: Promise<T>, timeoutMs: number, message: string): Promise<T> {
    let timeout: ReturnType<typeof setTimeout> | undefined;
    return Promise.race([
      promise,
      new Promise<T>((_resolve, reject) => {
        timeout = setTimeout(() => reject(new Error(message)), timeoutMs);
      }),
    ]).finally(() => {
      if (timeout !== undefined) clearTimeout(timeout);
    });
  }

  private stopPreview(): void {
    this.captureResolutionRequestId++;
    this.previewRequestId++;
    this.previewLoading = false;
    this.previewRequestDisplayId = null;
    this.previewAttemptedDisplayId = null;
    this.previewMessage = 'Select a display to start its live preview.';
    this.releasePreviewStream();
  }

  private releasePreviewStream(): void {
    this.previewStream?.getTracks().forEach((track) => track.stop());
    this.previewStream = null;
    this.previewDisplayId = null;
    if (this.status?.captureMode !== 'custom') this.pauseFramePump();
  }

  private syncFrameTransport(): void {
    if (!this.status?.isRunning || this.status.captureMode === 'audio') {
      this.stopFrameTransport();
      return;
    }
    if (this.status.captureMode !== 'custom' &&
        (!this.previewStream || !this.captureVideo?.nativeElement)) {
      this.pauseFramePump();
      return;
    }
    if (this.frameSocket?.readyState === WebSocket.OPEN) {
      this.startFramePump();
      return;
    }
    if (this.frameSocket?.readyState === WebSocket.CONNECTING) return;

    const socket = new WebSocket(this.api.frameSocketUrl());
    socket.binaryType = 'arraybuffer';
    this.frameSocket = socket;
    socket.onopen = () => {
      if (this.frameSocket !== socket) {
        socket.close();
        return;
      }
      this.startFramePump();
    };
    socket.onerror = () => {
      this.zone.run(() => this.showError(new Error('Unable to connect the RGB frame stream to the sync service.')));
    };
    socket.onclose = (event) => {
      if (this.frameSocket !== socket) return;
      this.frameSocket = null;
      this.pauseFramePump();
      if (this.status?.isRunning && event.code !== 1000) {
        this.zone.run(() => this.showError(new Error(event.reason || 'The RGB frame stream disconnected.')));
      }
    };
  }

  private startFramePump(): void {
    if (!this.frameSocket || this.frameSocket.readyState !== WebSocket.OPEN) return;
    const frameRate = this.effectiveFrameRate();
    if (
      this.frameTimerRate === frameRate &&
      this.frameTimer !== null
    ) return;
    this.pauseFramePump();
    const video = this.captureVideo?.nativeElement;
    const interval = Math.max(1, Math.round(1000 / frameRate));
    this.frameTimerRate = frameRate;
    if (this.status?.captureMode === 'custom') {
      this.customEffectAnimator.start(performance.now());
      this.zone.runOutsideAngular(() => {
        this.frameTimer = setInterval(() => this.sendRgbFrame(), interval);
      });
      return;
    }
    if (!video) return;
    void video.play().catch((error: unknown) => this.zone.run(() => this.showError(error)));
    this.zone.runOutsideAngular(() => {
      this.frameTimer = setInterval(() => this.sendRgbFrame(), interval);
    });
  }

  private effectiveFrameRate(): number {
    const settings = this.status?.isRunning && this.status.ledDevice
      ? this.status.ledDevice
      : this.ledDevice;
    const configured = this.status?.framesPerSecond ?? this.framesPerSecond;
    const packetBytes = 6 + (settings.ledCount * 3);
    const frameTimeMs = (packetBytes * 10 * 1000 / settings.baudRate) + (settings.ledCount * 0.03);
    const serialCapacity = Math.round((1000 / frameTimeMs) * 0.95);
    return Math.max(1, Math.min(configured, serialCapacity));
  }

  private sendRgbFrame(): void {
    const socket = this.frameSocket;
    if (!socket || socket.readyState !== WebSocket.OPEN) return;
    const frameLength = (this.frameLedDevice ?? this.ledDevice).ledCount * 3;
    // Never queue stale frames: bounded buffering keeps controller output close to the live capture.
    if (socket.bufferedAmount >= frameLength) return;
    const frame = this.status?.captureMode === 'custom'
      ? this.createCustomFrame(performance.now())
      : this.sampleLedFrame();
    if (!frame) return;
    try {
      socket.send(frame);
    } catch (error: unknown) {
      this.zone.run(() => this.showError(error));
      this.stopFrameTransport();
    }
  }

  private createCustomFrame(now: number): Uint8Array<ArrayBuffer> {
    const layout = this.frameLedDevice ?? this.ledDevice;
    const frame = this.customEffectAnimator.createFrame({
      now,
      effect: this.customEffect,
      colors: this.customColors,
      speed: this.customSpeed,
      layout,
      getFrameBuffer: (length) => this.getRgbFrameBuffer(length),
    });
    this.presentLedFrame(frame, layout);
    return frame;
  }

  private sampleLedFrame(): Uint8Array<ArrayBuffer> | null {
    const video = this.captureVideo?.nativeElement;
    if (!video || video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA ||
        !video.videoWidth || !video.videoHeight) return null;

    // A fixed analysis canvas limits per-frame pixel work regardless of the monitor's native resolution.
    const width = 320;
    const height = 180;
    this.frameCanvas ??= document.createElement('canvas');
    if (this.frameCanvas.width !== width || this.frameCanvas.height !== height) {
      this.frameCanvas.width = width;
      this.frameCanvas.height = height;
      this.frameContext = null;
    }
    this.frameContext ??= this.frameCanvas.getContext('2d', { willReadFrequently: true });
    if (!this.frameContext) {
      this.zone.run(() => this.showError(new Error('Unable to initialize the screen color sampler.')));
      this.stopFrameTransport();
      return null;
    }
    let pixels: Uint8ClampedArray;
    try {
      this.frameContext.drawImage(video, 0, 0, width, height);
      pixels = this.frameContext.getImageData(0, 0, width, height).data;
    } catch (error: unknown) {
      this.zone.run(() => this.showError(error));
      this.stopFrameTransport();
      return null;
    }
    const layout = this.frameLedDevice ?? this.ledDevice;
    const placements = calculateLedCardPlacements(layout);
    const output = this.getRgbFrameBuffer(layout.ledCount * 3);
    for (const placement of placements) {
      const mappedLed = mapLedIndex(placement.index, layout);
      const left = Math.max(0, Math.min(width - 1, Math.floor(placement.left / 100 * width)));
      const top = Math.max(0, Math.min(height - 1, Math.floor(placement.top / 100 * height)));
      const right = Math.max(left + 1, Math.min(width, Math.ceil((placement.left + placement.width) / 100 * width)));
      const bottom = Math.max(top + 1, Math.min(height, Math.ceil((placement.top + placement.height) / 100 * height)));
      let red = 0;
      let green = 0;
      let blue = 0;
      let samples = 0;
      for (let y = top; y < bottom; y++) {
        for (let x = left; x < right; x++) {
          const pixel = (y * width + x) * 4;
          red += pixels[pixel];
          green += pixels[pixel + 1];
          blue += pixels[pixel + 2];
          samples++;
        }
      }
      output[mappedLed * 3] = Math.round(red / samples);
      output[mappedLed * 3 + 1] = Math.round(green / samples);
      output[mappedLed * 3 + 2] = Math.round(blue / samples);
    }

    this.presentLedFrame(output, layout, placements);
    return output;
  }

  private getRgbFrameBuffer(length: number): Uint8Array<ArrayBuffer> {
    if (this.rgbFrameBuffer.length !== length)
      this.rgbFrameBuffer = new Uint8Array(new ArrayBuffer(length));
    else
      this.rgbFrameBuffer.fill(0);
    return this.rgbFrameBuffer;
  }

  private presentLedFrame(
    output: Uint8Array<ArrayBuffer>,
    layout: LedDeviceSettings,
    placements = calculateLedCardPlacements(layout),
  ): void {
    this.applyLedOutputProcessing(output);
    if (document.hidden) return;
    this.drawLedFrame(output, layout, placements);
  }

  private renderLatestLedFrame(): void {
    if (document.hidden || !this.previousOutputFrame) return;
    const layout = this.frameLedDevice ?? this.ledDevice;
    this.drawLedFrame(
      this.previousOutputFrame,
      layout,
      calculateLedCardPlacements(layout),
    );
  }

  private drawLedFrame(
    output: Uint8Array<ArrayBuffer>,
    layout: LedDeviceSettings,
    placements: LedCardPlacement[],
  ): void {
    if (this.section === 'dashboard') {
      drawLedCardCanvas(this.dashboardLedCanvas?.nativeElement, layout, placements, output);
    } else if (this.section === 'profiles') {
      const editorLayout = this.ledDevice;
      drawLedCardCanvas(
        this.profileLedCanvas?.nativeElement,
        editorLayout,
        calculateLedCardPlacements(editorLayout),
        output,
      );
    }
  }

  private applyLedOutputProcessing(frame: Uint8Array<ArrayBuffer>): void {
    const settings = this.ledOutputSettings;
    const count = Math.floor(frame.length / 3);
    if (this.blackLedMask.length !== count) this.blackLedMask = new Uint8Array(count);
    else this.blackLedMask.fill(0);
    const black = this.blackLedMask;
    let averageRed = 0;
    let averageGreen = 0;
    let averageBlue = 0;
    let litCount = 0;

    // Capture exact black LEDs before averaging or smoothing so configured dark gaps stay dark.
    for (let led = 0; led < count; led++) {
      const offset = led * 3;
      let red = frame[offset];
      let green = frame[offset + 1];
      let blue = frame[offset + 2];
      if (Math.max(red, green, blue) <= settings.blackLevelThreshold) {
        black[led] = 1;
        frame[offset] = 0;
        frame[offset + 1] = 0;
        frame[offset + 2] = 0;
        continue;
      }
      averageRed += red;
      averageGreen += green;
      averageBlue += blue;
      litCount++;
    }

    if (settings.averageColors && litCount > 0) {
      const red = Math.round(averageRed / litCount);
      const green = Math.round(averageGreen / litCount);
      const blue = Math.round(averageBlue / litCount);
      for (let led = 0; led < count; led++) {
        if (black[led]) continue;
        const offset = led * 3;
        frame[offset] = red;
        frame[offset + 1] = green;
        frame[offset + 2] = blue;
      }
    }

    const temperatureGains = settings.colorTemperatureEnabled
      ? this.getColorTemperatureGains(settings.colorTemperature)
      : this.neutralTemperatureGains;
    const smoothing = settings.smoothing / 255;
    const previous = this.previousOutputFrame?.length === frame.length
      ? this.previousOutputFrame
      : null;
    const brightnessScale = settings.brightness / 100;
    const brightnessLimit = settings.brightnessCap * 7.65;
    const gammaLookup = this.getGammaBrightnessLookup(
      settings.gamma,
      brightnessScale,
      temperatureGains,
    );

    for (let led = 0; led < count; led++) {
      const offset = led * 3;
      if (black[led]) {
        frame[offset] = 0;
        frame[offset + 1] = 0;
        frame[offset + 2] = 0;
        continue;
      }

      let red = gammaLookup[frame[offset]];
      let green = gammaLookup[256 + frame[offset + 1]];
      let blue = gammaLookup[512 + frame[offset + 2]];

      const brightestChannel = Math.max(red, green, blue);
      if (brightestChannel > 0 && settings.overBrighten > 0) {
        const target = brightestChannel + (255 - brightestChannel) * settings.overBrighten / 100;
        const scale = target / brightestChannel;
        red *= scale;
        green *= scale;
        blue *= scale;
      }

      const sum = red + green + blue;
      if (sum > brightnessLimit) {
        const scale = brightnessLimit / sum;
        red *= scale;
        green *= scale;
        blue *= scale;
      }

      if (previous) {
        red = red * (1 - smoothing) + previous[offset] * smoothing;
        green = green * (1 - smoothing) + previous[offset + 1] * smoothing;
        blue = blue * (1 - smoothing) + previous[offset + 2] * smoothing;
      }
      frame[offset] = Math.round(Math.min(255, Math.max(0, red)));
      frame[offset + 1] = Math.round(Math.min(255, Math.max(0, green)));
      frame[offset + 2] = Math.round(Math.min(255, Math.max(0, blue)));
    }

    if (this.previousOutputFrame?.length !== frame.length)
      this.previousOutputFrame = new Uint8Array(frame.length);
    this.previousOutputFrame.set(frame);
  }

  private getGammaBrightnessLookup(
    gamma: number,
    brightnessScale: number,
    gains: [number, number, number],
  ): Float64Array {
    if (this.cachedGamma === gamma &&
        this.cachedBrightnessScale === brightnessScale &&
        this.cachedLookupGains[0] === gains[0] &&
        this.cachedLookupGains[1] === gains[1] &&
        this.cachedLookupGains[2] === gains[2]) {
      return this.gammaBrightnessLookup;
    }

    for (let channel = 0; channel < 3; channel++) {
      const gain = gains[channel];
      const channelOffset = channel * 256;
      for (let value = 0; value < 256; value++) {
        this.gammaBrightnessLookup[channelOffset + value] =
          Math.pow(value * gain / 255, gamma) * 255 * brightnessScale;
      }
      this.cachedLookupGains[channel] = gain;
    }
    this.cachedGamma = gamma;
    this.cachedBrightnessScale = brightnessScale;
    return this.gammaBrightnessLookup;
  }

  private getColorTemperatureGains(temperature: number): [number, number, number] {
    if (temperature === this.cachedTemperature) return this.cachedTemperatureGains;

    const white = (kelvin: number): [number, number, number] => {
      const value = kelvin / 100;
      const red = value <= 66
        ? 255
        : 329.698727446 * Math.pow(value - 60, -0.1332047592);
      const green = value <= 66
        ? 99.4708025861 * Math.log(value) - 161.1195681661
        : 288.1221695283 * Math.pow(value - 60, -0.0755148492);
      const blue = value >= 66
        ? 255
        : value <= 19
          ? 0
          : 138.5177312231 * Math.log(value - 10) - 305.044792731;
      return [
        Math.min(255, Math.max(0, red)),
        Math.min(255, Math.max(0, green)),
        Math.min(255, Math.max(0, blue)),
      ];
    };
    const reference = white(6500);
    const target = white(temperature);
    this.cachedTemperatureGains = [
      target[0] / reference[0],
      target[1] / reference[1],
      target[2] / reference[2],
    ];
    this.cachedTemperature = temperature;
    return this.cachedTemperatureGains;
  }

  private pauseFramePump(): void {
    if (this.frameTimer !== null) {
      clearInterval(this.frameTimer);
      this.frameTimer = null;
    }
    this.frameTimerRate = 0;
  }

  private stopFrameTransport(): void {
    this.pauseFramePump();
    const socket = this.frameSocket;
    this.frameSocket = null;
    if (socket && socket.readyState < WebSocket.CLOSING)
      socket.close(1000, 'Synchronization stopped.');
    this.previousOutputFrame = null;
    this.customEffectAnimator.reset();
    this.frameContext = null;
    if (this.frameCanvas) {
      this.frameCanvas.width = 0;
      this.frameCanvas.height = 0;
    }
  }

  private showError(error: unknown): void {
    this.error = this.api.errorMessage(error);
    this.notice = '';
    this.markUiForCheck();
  }

  private clearMessages(): void {
    this.error = '';
    this.notice = '';
  }

  private markUiForCheck(): void {
    this.changeDetector.markForCheck();
  }
}
