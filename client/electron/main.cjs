const { app, BrowserWindow } = require('electron');
const path = require('path');
const { spawn } = require('child_process');

let mainWindow;
let apiProcess;

function createWindow() {
    mainWindow = new BrowserWindow({
        width: 1000,
        height: 800,
        webPreferences: {
            nodeIntegration: true,
            contextIsolation: false
        }
    });

    // In dev, load from Vite dev server. In prod, load from file.
    const isDev = process.env.NODE_ENV === 'development';

    if (isDev) {
        mainWindow.loadURL('http://localhost:5173');
        mainWindow.webContents.openDevTools();
    } else {
        mainWindow.loadFile(path.join(__dirname, '../dist/index.html'));
    }

    mainWindow.on('closed', function () {
        mainWindow = null;
    });
}

function startApi() {
    const isDev = process.env.NODE_ENV === 'development';

    if (isDev) {
        const projectPath = path.join(__dirname, '../../BatchDownloader.API.csproj');
        console.log("Starting API from source: " + projectPath);
        apiProcess = spawn('dotnet', ['run', '--project', projectPath, '--urls=http://localhost:5000'], {
            shell: true,
            env: { ...process.env, "ASPNETCORE_ENVIRONMENT": "Development" }
        });
    } else {
        const apiExePath = process.platform === 'win32'
            ? path.join(process.resourcesPath, 'api', 'BatchDownloader.API.exe')
            : path.join(process.resourcesPath, 'api', 'BatchDownloader.API');

        console.log("Starting API from binary: " + apiExePath);
        apiProcess = spawn(apiExePath, ['--urls=http://localhost:5000'], {
            env: { ...process.env, "ASPNETCORE_ENVIRONMENT": "Production" }
        });
    }

    apiProcess.stdout.on('data', (data) => {
        console.log(`API: ${data}`);
    });

    apiProcess.stderr.on('data', (data) => {
        console.error(`API Error: ${data}`);
    });
}

app.on('ready', () => {
    startApi();
    createWindow();
});

app.on('window-all-closed', function () {
    if (process.platform !== 'darwin') app.quit();
});

app.on('activate', function () {
    if (mainWindow === null) createWindow();
});

app.on('will-quit', () => {
    if (apiProcess) {
        console.log("Killing API process...");
        if (process.platform === 'win32') {
            // taskkill is more reliable for killing process trees on Windows
            spawn('taskkill', ['/pid', apiProcess.pid, '/f', '/t']);
        } else {
            apiProcess.kill();
        }
    }
});
