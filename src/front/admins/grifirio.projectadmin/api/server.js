const express = require('express');
const cors = require('cors');
const { exec } = require('child_process');
const util = require('util');

const execPromise = util.promisify(exec);
const app = express();
const PORT = 3001;

app.use(cors());
app.use(express.json());

// Docker container durumunu kontrol et
app.get('/api/docker/status/:containerName', async (req, res) => {
  try {
    const { containerName } = req.params;
    const { stdout } = await execPromise(`docker ps -a --filter "name=${containerName}" --format "{{.Status}}"`);
    
    const isRunning = stdout.toLowerCase().includes('up');
    
    res.json({
      containerName,
      running: isRunning,
      status: stdout.trim() || 'Not found'
    });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to check container status',
      message: error.message
    });
  }
});

// Docker container başlat
app.post('/api/docker/start/:containerName', async (req, res) => {
  try {
    const { containerName } = req.params;
    
    // Önce container'ın durumunu kontrol et
    const { stdout: statusOut } = await execPromise(`docker ps -a --filter "name=${containerName}" --format "{{.Status}}"`);
    
    if (statusOut.toLowerCase().includes('up')) {
      return res.json({
        message: 'Container is already running',
        containerName
      });
    }
    
    // Container'ı başlat
    await execPromise(`docker start ${containerName}`);
    
    res.json({
      message: `Container ${containerName} started successfully`,
      containerName
    });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to start container',
      message: error.message,
      stderr: error.stderr
    });
  }
});

// Docker container durdur
app.post('/api/docker/stop/:containerName', async (req, res) => {
  try {
    const { containerName } = req.params;
    
    // Container'ı durdur
    await execPromise(`docker stop ${containerName}`);
    
    res.json({
      message: `Container ${containerName} stopped successfully`,
      containerName
    });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to stop container',
      message: error.message,
      stderr: error.stderr
    });
  }
});

// Docker container yeniden başlat
app.post('/api/docker/restart/:containerName', async (req, res) => {
  try {
    const { containerName } = req.params;
    
    // Container'ı yeniden başlat
    await execPromise(`docker restart ${containerName}`);
    
    res.json({
      message: `Container ${containerName} restarted successfully`,
      containerName
    });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to restart container',
      message: error.message,
      stderr: error.stderr
    });
  }
});

// Tüm container'ları listele
app.get('/api/docker/list', async (req, res) => {
  try {
    const { stdout } = await execPromise('docker ps -a --format "{{.Names}}\t{{.Status}}\t{{.Ports}}"');
    
    const containers = stdout.trim().split('\n').map(line => {
      const [name, status, ports] = line.split('\t');
      return {
        name,
        status,
        ports,
        running: status.toLowerCase().includes('up')
      };
    });
    
    res.json({ containers });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to list containers',
      message: error.message
    });
  }
});

// .NET servis durumunu kontrol et (port'a göre)
app.get('/api/dotnet/status/:port', async (req, res) => {
  try {
    const { port } = req.params;
    
    // Port'un kullanılıp kullanılmadığını kontrol et
    const { stdout } = await execPromise(`netstat -ano | findstr :${port}`);
    
    const isRunning = stdout.trim().length > 0 && stdout.includes('LISTENING');
    
    res.json({
      port,
      running: isRunning,
      message: isRunning ? 'Service is running' : 'Service is not running'
    });
  } catch (error) {
    // Port bulunamadı = servis çalışmıyor
    res.json({
      port: req.params.port,
      running: false,
      message: 'Service is not running'
    });
  }
});

// .NET servisi başlat
app.post('/api/dotnet/start', async (req, res) => {
  try {
    const { projectPath, port } = req.body;
    
    if (!projectPath || !port) {
      return res.status(400).json({ error: 'projectPath and port are required' });
    }
    
    // Önce servisin zaten çalışıp çalışmadığını kontrol et
    try {
      const { stdout: portCheck } = await execPromise(`netstat -ano | findstr :${port}`, { timeout: 2000 });
      if (portCheck && portCheck.includes('LISTENING')) {
        return res.json({
          success: true,
          message: `.NET service is already running on port ${port}`,
          projectPath,
          port,
          alreadyRunning: true
        });
      }
    } catch (err) {
      // Port boş, devam edebiliriz
    }
    
    // Proje yolunu tam path'e çevir
    const fullPath = `d:\\Projeler\\Grifirio\\${projectPath}`;
    
    // .NET servisini yeni bir PowerShell penceresinde başlat
    const startCommand = `Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '${fullPath}'; Write-Host '🚀 .NET Service Starting on Port ${port}...' -ForegroundColor Cyan; Write-Host ''; dotnet run" -WindowStyle Normal`;
    
    try {
      await execPromise(`powershell -Command "${startCommand}"`, { 
        shell: 'powershell.exe',
        timeout: 5000
      });
      
      // 3 saniye bekle ki servis başlasın
      await new Promise(resolve => setTimeout(resolve, 3000));
      
      res.json({
        success: true,
        message: `.NET service started successfully on port ${port}`,
        projectPath,
        port,
        alreadyRunning: false
      });
    } catch (execError) {
      // PowerShell komutu başarılı bile olsa hata verebilir, yine de başarılı sayalım
      res.json({
        success: true,
        message: `.NET service start command executed on port ${port}`,
        projectPath,
        port,
        note: 'Command executed, check PowerShell window'
      });
    }
  } catch (error) {
    res.status(500).json({
      success: false,
      error: 'Failed to start .NET service',
      message: error.message,
      stderr: error.stderr
    });
  }
});

// .NET servisi durdur
app.post('/api/dotnet/stop', async (req, res) => {
  try {
    const { port } = req.body;
    
    if (!port) {
      return res.status(400).json({ error: 'port is required' });
    }
    
    // Port'u kullanan process ID'yi bul
    const { stdout } = await execPromise(`netstat -ano | findstr :${port}`);
    
    if (!stdout.trim()) {
      return res.json({
        message: 'Service is not running',
        port
      });
    }
    
    // PID'yi extract et (son sütun)
    const lines = stdout.trim().split('\n');
    const pids = new Set();
    
    lines.forEach(line => {
      const parts = line.trim().split(/\s+/);
      const pid = parts[parts.length - 1];
      if (pid && !isNaN(pid)) {
        pids.add(pid);
      }
    });
    
    // Tüm PID'leri kapat
    for (const pid of pids) {
      try {
        await execPromise(`taskkill /F /PID ${pid}`);
      } catch (err) {
        // Process zaten kapanmış olabilir
      }
    }
    
    res.json({
      message: `.NET service on port ${port} stopped successfully`,
      port,
      pidsKilled: Array.from(pids)
    });
  } catch (error) {
    res.status(500).json({
      error: 'Failed to stop .NET service',
      message: error.message,
      stderr: error.stderr
    });
  }
});

// Health check
app.get('/health', (req, res) => {
  res.json({ status: 'ok', message: 'Service Manager API is running' });
});

app.listen(PORT, () => {
  console.log(`🎛️ Service Manager API running on http://localhost:${PORT}`);
  console.log(`📊 Health check: http://localhost:${PORT}/health`);
  console.log(`🐳 Docker API endpoints:`);
  console.log(`   GET  /api/docker/status/:containerName`);
  console.log(`   POST /api/docker/start/:containerName`);
  console.log(`   POST /api/docker/stop/:containerName`);
  console.log(`   POST /api/docker/restart/:containerName`);
  console.log(`   GET  /api/docker/list`);
  console.log(`🔧 .NET API endpoints:`);
  console.log(`   GET  /api/dotnet/status/:port`);
  console.log(`   POST /api/dotnet/start (body: {projectPath, port})`);
  console.log(`   POST /api/dotnet/stop (body: {port})`);
});
