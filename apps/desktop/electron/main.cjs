const { app, BrowserWindow, desktopCapturer, dialog, ipcMain, Menu, net, protocol, screen, Tray } = require('electron');
const { spawn } = require('node:child_process');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const serviceUrl = 'http://127.0.0.1:5078';
let serviceProcess;
let mainWindow;
let splashWindow;
let tray;
let quitting = false;
let serviceLaunchError;
let serviceShutdownStarted = false;
let notifyUiReady;
const uiReady = new Promise((resolve) => {
  notifyUiReady = resolve;
});

app.setName('AuraSync');

protocol.registerSchemesAsPrivileged([
  { scheme: 'aurasync', privileges: { standard: true, secure: true, supportFetchAPI: true } },
]);

const hasSingleInstanceLock = app.requestSingleInstanceLock();
if (!hasSingleInstanceLock) app.quit();
else {
  app.on('second-instance', () => {
    showMainWindow();
  });
}

function showMainWindow() {
  if (!mainWindow || mainWindow.isDestroyed()) return;
  if (mainWindow.isMinimized()) mainWindow.restore();
  mainWindow.show();
  mainWindow.focus();
}

function createTray() {
  const iconPath = app.isPackaged
    ? path.join(process.resourcesPath, 'browser', 'aurasync-icon.ico')
    : path.resolve(__dirname, '../../../aurasync-icon.ico');
  tray = new Tray(iconPath);
  tray.setToolTip('AuraSync');
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Open AuraSync', click: showMainWindow },
    { type: 'separator' },
    { label: 'Quit', click: () => app.quit() },
  ]));
  tray.on('click', showMainWindow);
  tray.on('double-click', showMainWindow);
}

function startService() {
  if (app.isPackaged) {
    const executable = path.join(process.resourcesPath, 'service', 'LedSync.Api.exe');
    serviceProcess = spawn(executable, [], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  } else {
    const project = path.resolve(__dirname, '../../led-sync-service/src/LedSync.Api/LedSync.Api.csproj');
    serviceProcess = spawn('dotnet', ['run', '--project', project, '--no-launch-profile'], {
      cwd: path.dirname(project),
      windowsHide: true,
      stdio: ['ignore', 'pipe', 'pipe'],
      env: { ...process.env, ASPNETCORE_URLS: serviceUrl },
    });
  }

  serviceProcess.stdout.on('data', (data) => console.log(`[service] ${data}`));
  serviceProcess.stderr.on('data', (data) => console.error(`[service] ${data}`));
  serviceProcess.once('error', (error) => {
    serviceLaunchError = error;
    console.error('Unable to launch the AuraSync service.', error);
  });
}

function compareDisplayPosition(left, right) {
  return left.bounds.y - right.bounds.y || left.bounds.x - right.bounds.x;
}

function compareServiceDisplayPosition(left, right) {
  return left.y - right.y || left.x - right.x;
}

async function getScreenCaptureSourceId(displayId) {
  if (typeof displayId !== 'string' || displayId.length === 0) {
    throw new TypeError('A display must be selected before starting the preview.');
  }
  console.log(`[capture] Resolving screen source for ${JSON.stringify(displayId)}.`);

  const response = await fetch(`${serviceUrl}/api/v1/displays`);
  if (!response.ok) {
    throw new Error(`Unable to read available displays (HTTP ${response.status}).`);
  }

  const serviceDisplays = await response.json();
  // Electron and the service expose different display IDs; align them by virtual-desktop position.
  const selectedIndex = serviceDisplays
    .slice()
    .sort(compareServiceDisplayPosition)
    .findIndex((display) => display.id === displayId);
  if (selectedIndex < 0) {
    throw new Error('The selected display is no longer available. Refresh the display list and try again.');
  }

  const electronDisplays = screen.getAllDisplays().slice().sort(compareDisplayPosition);
  if (electronDisplays.length !== serviceDisplays.length) {
    throw new Error('The desktop capture and sync service detected different display counts. Refresh displays and try again.');
  }

  const selectedElectronDisplay = electronDisplays[selectedIndex];
  const sources = await desktopCapturer.getSources({
    types: ['screen'],
    thumbnailSize: { width: 1, height: 1 },
  });
  const source = sources.find((item) => item.display_id === String(selectedElectronDisplay.id));
  if (!source) {
    throw new Error('Electron could not find a capture source for the selected display.');
  }

  console.log(`[capture] Resolved ${JSON.stringify(displayId)} to ${source.id}.`);
  return source.id;
}

function registerCaptureHandlers() {
  ipcMain.handle('capture:get-screen-source-id', (event, displayId) => {
    const url = event.senderFrame?.url ?? '';
    // Capture is privileged: only the packaged UI or fixed local development origin may request it.
    const trusted = app.isPackaged
      ? url.startsWith('aurasync://app/')
      : url.startsWith('http://127.0.0.1:4200/');
    if (!trusted) throw new Error('Screen capture is available only to the AuraSync desktop UI.');

    return getScreenCaptureSourceId(displayId);
  });
  ipcMain.handle('app:ui-ready', (event) => {
    const url = event.senderFrame?.url ?? '';
    const trusted = app.isPackaged
      ? url.startsWith('aurasync://app/')
      : url.startsWith('http://127.0.0.1:4200/');
    if (!trusted) throw new Error('Only the AuraSync desktop UI can complete startup.');

    notifyUiReady?.();
    notifyUiReady = undefined;
    return true;
  });
}

async function waitForService() {
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (serviceLaunchError) throw serviceLaunchError;
    if (serviceProcess.exitCode !== null) {
      throw new Error(`The AuraSync service exited with code ${serviceProcess.exitCode}.`);
    }
    try {
      const response = await fetch(`${serviceUrl}/api/v1/health`);
      if (response.ok) {
        const health = await response.json();
        if (health.service === 'aurasync' && health.status === 'healthy') return;
      }
    } catch {
      await new Promise((resolve) => setTimeout(resolve, 500));
    }
  }
  throw new Error('Timed out waiting for the AuraSync service to become ready.');
}

async function createSplashWindow() {
  splashWindow = new BrowserWindow({
    width: 520,
    height: 520,
    frame: false,
    resizable: false,
    maximizable: false,
    minimizable: false,
    backgroundColor: '#05050a',
    alwaysOnTop: true,
    show: false,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
    },
  });
  await splashWindow.loadFile(path.join(__dirname, 'splash-screen.html'));
  splashWindow.show();
}

async function createWindow(showWhenReady = false) {
  mainWindow = new BrowserWindow({
    width: 1440,
    height: 940,
    minWidth: 860,
    minHeight: 620,
    title: 'AuraSync',
    icon: path.resolve(__dirname, '../../../aurasync-icon.ico'),
    backgroundColor: '#0b1018',
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      backgroundThrottling: false,
    },
  });
  mainWindow.on('close', (event) => {
    if (!quitting) {
      event.preventDefault();
      mainWindow.hide();
    }
  });

  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  mainWindow.webContents.on('will-navigate', (event, url) => {
    const allowed = app.isPackaged
      ? url.startsWith('aurasync://app/')
      : url.startsWith('http://127.0.0.1:4200/');
    if (!allowed) event.preventDefault();
  });

  if (app.isPackaged) {
    await mainWindow.loadURL('aurasync://app/index.html');
  } else {
    await mainWindow.loadURL('http://127.0.0.1:4200/');
  }
  mainWindow.on('closed', () => {
    mainWindow = undefined;
  });
  if (showWhenReady) mainWindow.show();
}

if (hasSingleInstanceLock) app.whenReady().then(async () => {
  try {
    Menu.setApplicationMenu(null);
    createTray();
    await createSplashWindow();
    registerCaptureHandlers();
    if (app.isPackaged) {
      const appDirectory = path.resolve(process.resourcesPath, 'browser');
      protocol.handle('aurasync', (request) => {
        const requestPath = decodeURIComponent(new URL(request.url).pathname);
        const filePath = path.resolve(appDirectory, `.${requestPath}`);
        // Reject traversal outside the bundled renderer before handing the path to Electron's file loader.
        if (filePath !== appDirectory && !filePath.startsWith(`${appDirectory}${path.sep}`))
          return new Response('Not found', { status: 404 });
        return net.fetch(pathToFileURL(filePath).toString());
      });
    }
    startService();
    await waitForService();
    await createWindow();
    await uiReady;
    if (mainWindow && !mainWindow.isDestroyed()) mainWindow.show();
    if (splashWindow && !splashWindow.isDestroyed()) splashWindow.close();
    splashWindow = undefined;
    app.on('activate', () => {
      if (!mainWindow || mainWindow.isDestroyed()) {
        createWindow(true).catch(showStartupError);
        return;
      }
      showMainWindow();
    });
  } catch (error) {
    showStartupError(error);
  }
});

function showStartupError(error) {
  console.error('AuraSync startup failed.', error);
  if (splashWindow && !splashWindow.isDestroyed()) splashWindow.close();
  splashWindow = undefined;
  dialog.showErrorBox(
    'AuraSync could not start',
    error instanceof Error ? error.message : 'An unexpected startup error occurred.',
  );
  app.quit();
}

app.on('before-quit', (event) => {
  quitting = true;
  tray?.destroy();
  tray = undefined;
  if (!serviceProcess || serviceProcess.exitCode !== null) return;

  event.preventDefault();
  if (serviceShutdownStarted) return;
  serviceShutdownStarted = true;
  serviceProcess.once('exit', () => app.quit());
  if (process.platform !== 'win32') {
    serviceProcess.kill();
    return;
  }

  const killer = spawn('taskkill.exe', ['/PID', String(serviceProcess.pid), '/T', '/F'], {
    windowsHide: true,
    stdio: 'ignore',
  });
  killer.once('error', (error) => {
    console.error('Unable to stop the AuraSync service process tree.', error);
    serviceProcess.kill();
  });
  killer.once('close', (code) => {
    if (code !== 0 && serviceProcess.exitCode === null) {
      console.error(`Unable to stop the AuraSync service process tree (taskkill exited with code ${code}).`);
      serviceProcess.kill();
    }
  });
});

app.on('window-all-closed', () => {
  if (!quitting) app.quit();
});
