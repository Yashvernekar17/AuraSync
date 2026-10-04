const { contextBridge, ipcRenderer } = require('electron');

// Expose only the small desktop capability surface required by the sandboxed renderer.
contextBridge.exposeInMainWorld('ledSyncDesktop', Object.freeze({
  platform: process.platform,
  version: process.env.npm_package_version ?? '0.1.0',
  getScreenCaptureSourceId: (displayId) =>
    ipcRenderer.invoke('capture:get-screen-source-id', displayId),
  notifyUiReady: () => ipcRenderer.invoke('app:ui-ready'),
}));
