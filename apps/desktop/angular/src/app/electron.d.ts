interface Window {
  ledSyncDesktop?: {
    platform: string;
    version: string;
    getScreenCaptureSourceId(displayId: string): Promise<string>;
    notifyUiReady(): Promise<boolean>;
  };
}
