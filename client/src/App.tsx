import { useState, useRef, useEffect } from 'react'
import './App.css'

// Backend Interfaces
interface DownloadRequest {
  destination: string;
  links: string[];
  concurrency: number;
  throttleBytesPerSecond: number;
}

interface ProgressMessage {
  Id: number;
  Url: string;
  Status: string;
  BytesReceived: number;
  TotalBytes: number;
  Error?: string;
  LocalPath?: string;
}

function App() {
  const [connected, setConnected] = useState(false);
  const [apiUrl, setApiUrl] = useState(localStorage.getItem('apiUrl') || 'http://localhost:5000');
  const [apiKey, setApiKey] = useState(localStorage.getItem('apiKey') || 'secret-key');
  const [downloadDir, setDownloadDir] = useState('');
  const [healthStatus, setHealthStatus] = useState<'unknown' | 'ok' | 'fail'>('unknown');

  useEffect(() => {
    // Quick health check on mount if we have a URL
    const checkHealth = async () => {
      if (!apiUrl) return;
      try {
        console.log(`Checking health: ${apiUrl}/health`);
        const res = await fetch(`${apiUrl}/health`, {
          headers: { 'X-API-KEY': apiKey }
        });
        if (res.ok) {
          setHealthStatus('ok');
        } else {
          console.warn("Health check failed with status:", res.status);
          setHealthStatus('fail');
        }
      } catch (e) {
        console.error("Health check fetch error:", e);
        setHealthStatus('fail');
      }
    };
    checkHealth();
  }, []);

  // Downloads State
  const [downloads, setDownloads] = useState<Record<number, ProgressMessage>>({});
  const socketRef = useRef<WebSocket | null>(null);

  const saveSettings = (url: string, key: string) => {
    localStorage.setItem('apiUrl', url);
    localStorage.setItem('apiKey', key);
  };

  const connect = async () => {
    saveSettings(apiUrl, apiKey);
    try {
      // 1. Try to get download directory via HTTP to verify connection
      const res = await fetch(`${apiUrl}/getDownloadDirectory`, {
        headers: { 'X-API-KEY': apiKey }
      });

      if (!res.ok) {
        alert("Failed to connect: " + res.statusText);
        return;
      }

      const data = await res.json();
      setDownloadDir(data.downloadDir);

      // 2. Connect WebSocket
      const wsUrl = apiUrl.replace("http", "ws") + "/ws";
      const ws = new WebSocket(wsUrl);
      // Note: WS specific headers not supported in browser API easily, 
      // but we skipped auth for WS handshake in backend for now or passed via query param if needed.
      // If we need auth, we can pass ?key=... or do handshake. For now backend allows WS.

      ws.onopen = () => {
        setConnected(true);
        console.log("Connected to WS");
      };

      ws.onmessage = (event) => {
        const msg = JSON.parse(event.data) as ProgressMessage;
        if (msg.Status === 'removed') {
          setDownloads(prev => {
            const next = { ...prev };
            delete next[msg.Id];
            return next;
          });
        } else {
          setDownloads(prev => ({
            ...prev,
            [msg.Id]: msg
          }));
        }
      };

      ws.onclose = () => {
        setConnected(false);
        console.log("Disconnected from WS");
      };

      socketRef.current = ws;

    } catch (e) {
      alert("Connection Error: " + e);
    }
  };

  const startDownload = async (links: string[], dest: string, concurrency: number, throttle: number) => {
    try {
      const payload: DownloadRequest = {
        destination: dest,
        links: links,
        concurrency: concurrency,
        throttleBytesPerSecond: throttle * 1024 // Convert KB/s to Bytes/s
      };

      const res = await fetch(`${apiUrl}/downloads`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-API-KEY': apiKey
        },
        body: JSON.stringify(payload)
      });

      if (!res.ok) {
        const err = await res.json();
        alert("Download Failed to Start: " + (err.error || res.statusText));
      } else {
        // Success, IDs returned in map if needed
      }

    } catch (e) {
      alert("Error starting download: " + e);
    }
  };

  const cancelDownload = (id: number) => {
    if (socketRef.current && socketRef.current.readyState === WebSocket.OPEN) {
      socketRef.current.send(JSON.stringify({ command: 'cancel', id: id }));
    }
  };

  const resumeDownload = (id: number) => {
    if (socketRef.current && socketRef.current.readyState === WebSocket.OPEN) {
      socketRef.current.send(JSON.stringify({ command: 'resume', id: id }));
    }
  };

  const removeDownload = (id: number) => {
    if (socketRef.current && socketRef.current.readyState === WebSocket.OPEN) {
      socketRef.current.send(JSON.stringify({ command: 'remove', id: id }));
    }
    setDownloads(prev => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
  };

  const clearDownloads = () => {
    if (socketRef.current && socketRef.current.readyState === WebSocket.OPEN) {
      socketRef.current.send(JSON.stringify({ command: 'clear' }));
    }
    setDownloads({});
  };

  const shutdownServer = async () => {
    if (!window.confirm("Are you sure you want to stop the API server? You will need to restart it manually.")) return;
    try {
      await fetch(`${apiUrl}/shutdown`, {
        method: 'POST',
        headers: { 'X-API-KEY': apiKey }
      });
      setConnected(false);
      setHealthStatus('fail');
    } catch (e) {
      alert("Shutdown failed: " + e);
    }
  };

  const isMixedContent = window.location.protocol === 'https:' && apiUrl.startsWith('http://');

  return (
    <div className="container">
      {!connected ? (
        <div className="card" style={{ maxWidth: '500px', margin: 'auto' }}>
          <h2>Connect to Download Agent</h2>
          <p style={{ fontSize: '0.9em', color: '#888', marginBottom: '1.5em' }}>
            This application requires the <strong>Batch Downloader API</strong> to be running on your computer to access the file system.
          </p>

          <div className="form-group">
            <label>API URL</label>
            <input type="text" value={apiUrl} onChange={e => setApiUrl(e.target.value)} style={{ width: '100%' }} placeholder="http://localhost:5000" />
          </div>

          <div className="form-group">
            <label>API Key</label>
            <input type="password" value={apiKey} onChange={e => setApiKey(e.target.value)} style={{ width: '100%' }} />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5em', marginTop: '1em', fontSize: '0.85em' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5em' }}>
              <span>Status check:</span>
              {healthStatus === 'unknown' && <span style={{ color: '#888' }}>Checking...</span>}
              {healthStatus === 'ok' && <span style={{ color: '#4caf50' }}>● Agent Reachable</span>}
              {healthStatus === 'fail' && <span style={{ color: '#f44336' }}>● Agent Unreachable</span>}
            </div>

            {healthStatus === 'fail' && (
              <div style={{ color: '#fba', padding: '0.8em', background: '#322', borderRadius: '4px', marginTop: '0.5em' }}>
                <strong>Common Fixes:</strong>
                <ul style={{ margin: '0.5em 0', paddingLeft: '1.5em' }}>
                  {isMixedContent && <li>Enable "Insecure Content" in Site Settings (Lock Icon)</li>}
                  <li>Disable AdBlockers for this site (ERR_BLOCKED_BY_CLIENT)</li>
                  <li>Ensure the Standalone Agent is running on your PC</li>
                </ul>
              </div>
            )}
          </div>

          <button onClick={connect} className="primary-button" style={{ marginTop: '1em', width: '100%' }}>Connect to Agent</button>

          <div style={{ marginTop: '2em', paddingTop: '1em', borderTop: '1px solid #333' }}>
            <h4>Don't have the agent?</h4>
            <p style={{ fontSize: '0.8em', color: '#aaa' }}>
              Download the standalone API agent to run it without Electron.
            </p>
            <a href="https://github.com/ZeroPointGaming/BatchDownloader.API/releases" target="_blank" rel="noreferrer" style={{ color: '#4a9eff', textDecoration: 'none', fontWeight: 'bold' }}>
              Download Agent v1.1.0 (win-x64)
            </a>
          </div>
        </div>
      ) : (
        <>
          <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              <h1>Batch Downloader</h1>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5em', fontSize: '0.8em' }}>
                <span style={{ color: '#4caf50' }}>● Connected to Local Agent</span>
                <span style={{ color: '#888' }}>({apiUrl})</span>
              </div>
            </div>
            <div style={{ display: 'flex', gap: '0.5em' }}>
              <button onClick={shutdownServer} style={{ background: '#622', color: '#fff' }}>Stop Server</button>
              <button onClick={() => { socketRef.current?.close(); }}>Disconnect</button>
            </div>
          </header>

          <div className="card">
            <h3>New Download</h3>
            <p style={{ fontSize: '0.8em', color: '#888' }}>Downloads will be saved to: <strong>{downloadDir}</strong></p>
            <DownloadForm onSubmit={startDownload} />
          </div>

          <div className="card download-list">
            <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1em' }}>
              <h3>Active Downloads & Queue</h3>
              <button onClick={clearDownloads} style={{ padding: '0.4em 0.8em', fontSize: '0.8em', background: '#444' }}>Clear Finished</button>
            </header>
            {Object.values(downloads).sort((a, b) => b.Id - a.Id).map(d => (
              <DownloadItem
                key={d.Id}
                item={d}
                onCancel={() => cancelDownload(d.Id)}
                onResume={() => resumeDownload(d.Id)}
                onRemove={() => removeDownload(d.Id)}
              />
            ))}
            {Object.keys(downloads).length === 0 && <p>No downloads in progress or queue.</p>}
          </div>
        </>
      )}
    </div>
  );
}

function DownloadForm({ onSubmit }: { onSubmit: (l: string[], d: string, c: number, t: number) => void }) {
  const [links, setLinks] = useState('');
  const [dest, setDest] = useState('');
  const [concurrency, setConcurrency] = useState(3);
  const [throttle, setThrottle] = useState(0);

  const handleSubmit = () => {
    const list = links.split('\n').map(s => s.trim()).filter(s => s.length > 0);
    if (list.length === 0) return alert("Enter at least one link");
    onSubmit(list, dest, concurrency, throttle);
    setLinks('');
  };

  return (
    <div className="download-form-container">
      <div className="form-group">
        <label>Destination Folder (Relative to Root)</label>
        <input type="text" value={dest} onChange={e => setDest(e.target.value)} />
      </div>
      <div className="form-group">
        <label>Links (One per line)</label>
        <textarea rows={5} value={links} onChange={e => setLinks(e.target.value)} placeholder="http://example.com/file1.zip" />
      </div>
      <div className="form-row">
        <div className="form-group">
          <label>Max Parallel</label>
          <input type="number" min={1} max={10} value={concurrency} onChange={e => setConcurrency(parseInt(e.target.value))} />
        </div>
        <div className="form-group">
          <label>Throttle (KB/s, 0=None)</label>
          <input type="number" min={0} value={throttle} onChange={e => setThrottle(parseInt(e.target.value))} />
        </div>
      </div>
      <button onClick={handleSubmit} className="primary-button">Start Batch Download</button>
    </div>
  );
}

function DownloadItem({ item, onCancel, onResume, onRemove }: { item: ProgressMessage, onCancel: () => void, onResume: () => void, onRemove: () => void }) {
  const percent = item.TotalBytes && item.TotalBytes > 0
    ? Math.round((item.BytesReceived / item.TotalBytes) * 100)
    : 0;

  return (
    <div className="download-item">
      <div className="download-header">
        <div style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '70%' }}>
          #{item.Id} - {item.Url}
        </div>
        <div className={`status-${item.Status}`}>
          {item.Status} {(item.Status === 'downloading' || item.Status === 'completed') && `(${percent}%)`}
        </div>
      </div>
      {item.Status === 'downloading' && (
        <div className="progress-bar">
          <div className="progress-fill" style={{ width: `${percent}%` }}></div>
        </div>
      )}
      <div style={{ marginTop: '0.5em', display: 'flex', gap: '0.5em' }}>
        {item.Status === 'downloading' && (
          <button style={{ padding: '0.2em 0.5em', fontSize: '0.8em', background: '#552', width: 'auto' }} onClick={onCancel}>Stop</button>
        )}
        {item.Status === 'pending' && (
          <button style={{ padding: '0.2em 0.5em', fontSize: '0.8em', background: '#552', width: 'auto' }} onClick={onCancel}>Cancel</button>
        )}
        {(item.Status === 'stopped' || item.Status === 'error') && (
          <button style={{ padding: '0.2em 0.5em', fontSize: '0.8em', background: '#252', width: 'auto' }} onClick={onResume}>Resume</button>
        )}
        {(item.Status === 'stopped' || item.Status === 'error' || item.Status === 'completed' || item.Status === 'pending') && (
          <button style={{ padding: '0.2em 0.5em', fontSize: '0.8em', background: '#444', width: 'auto' }} onClick={onRemove}>Remove</button>
        )}
      </div>
      {item.Error && <div style={{ color: 'red', fontSize: '0.8em' }}>{item.Error}</div>}
    </div>
  );
}

export default App;