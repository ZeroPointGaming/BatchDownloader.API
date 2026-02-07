# 🚀 Batch Downloader Desktop

A modern, high-performance desktop application for managing batch file downloads. Built with a **React** frontend and a robust **ASP.NET Core** backend, all wrapped in **Electron** for a seamless native experience.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10-purple.svg)
![React](https://img.shields.io/badge/React-19-blue.svg)
![Electron](https://img.shields.io/badge/Electron-Latest-9feaf9.svg)

---

## Features

*   **Batch Processing**: Paste multiple links and download them all at once.
*   **Real-time Tracking**: Watch download progress (speed, percentage, status) via high-speed WebSockets.
*   **Stop & Resume**: Interrupt downloads and pick up exactly where you left off using HTTP Range headers.
*   **Concurrency Control**: Limit how many files download simultaneously (1-10) to manage system resources.
*   **Smart Throttling**: Set cumulative speed limits (KB/s) to save bandwidth for other tasks.
*   **Queue Management**: Remove individual items or clear all finished downloads with one click.
*   **Security First**:
    *   **API Key Protection**: Secure handshake between Electron and the .NET service.
    *   **Path Isolation**: Strict directory traversal prevention ensures files stay within allowed folders.
*   **Clean Lifecycle**: Robust shutdown protocol ensures no orphaned backend processes are left running on app exit.

---

## Installation & Setup

### Prerequisites

*   [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
*   [Node.js 18+](https://nodejs.org/)

### Local Development

1.  **Clone & Install**:
    ```bash
    git clone https://github.com/youruser/batch-downloader.git
    cd batch-downloader/client
    npm install
    ```

2.  **Run in Dev Mode**:
    ```bash
    npm run dev
    ```
    *This launches the .NET backend API and the Electron interface simultaneously.*

### Creating a Production Release

I've included an automation script to handle the multi-step build process (compiling .NET to native binary + bundling Electron).

1.  Open a terminal in the `client/` folder.
2.  Run the build script:
    ```powershell
    ./build-release.ps1
    ```
3.  Find your portable executable in `client/release/`.

---

## Docker Deployment

To run the backend as a headless service on a remote server/NAS:

```bash
docker-compose up --build
```

Access the API at `http://localhost:5000`. You can point your local Desktop client to this URL!

---

## Configuration

Settings in `appsettings.json`:

| Setting | Description | Default |
| :--- | :--- | :--- |
| `ApiKey` | Secret key for API access | `secret-key` |
| `FileSystem:RootPath` | Where files are saved | `C:\Downloads` |
