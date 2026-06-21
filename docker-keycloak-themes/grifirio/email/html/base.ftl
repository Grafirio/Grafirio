<#macro emailLayout>
<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
      background-color: #1a1a1a;
      color: rgba(255,255,255,0.87);
      line-height: 1.6;
      -webkit-font-smoothing: antialiased;
    }
    .wrapper {
      max-width: 560px;
      margin: 40px auto;
      padding: 0 16px;
    }
    .logo-bar {
      text-align: center;
      padding: 24px 0 20px;
    }
    .logo {
      display: inline-flex;
      align-items: center;
      gap: 10px;
      font-size: 1.25rem;
      font-weight: 700;
      color: rgba(255,255,255,0.87);
      text-decoration: none;
    }
    .logo-icon {
      width: 28px;
      height: 28px;
      background: #646cff;
      border-radius: 7px;
      display: inline-block;
    }
    .card {
      background: #2a2a2a;
      border: 1px solid #3a3a3a;
      border-radius: 10px;
      padding: 36px 40px;
      box-shadow: 0 4px 24px rgba(0,0,0,0.4);
    }
    h1 {
      font-size: 1.25rem;
      font-weight: 600;
      color: rgba(255,255,255,0.87);
      margin-bottom: 12px;
    }
    p {
      color: rgba(255,255,255,0.6);
      font-size: 0.9rem;
      margin-bottom: 20px;
    }
    .btn {
      display: inline-block;
      background: #646cff;
      color: white !important;
      text-decoration: none;
      padding: 12px 28px;
      border-radius: 7px;
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.01em;
      margin: 8px 0 20px;
      transition: background 0.2s;
    }
    .url-fallback {
      font-size: 0.75rem;
      color: rgba(255,255,255,0.35);
      word-break: break-all;
      background: #1e1e1e;
      border: 1px solid #3a3a3a;
      border-radius: 6px;
      padding: 8px 12px;
      margin-top: 8px;
    }
    .divider {
      border: none;
      border-top: 1px solid #3a3a3a;
      margin: 24px 0;
    }
    .footer {
      text-align: center;
      padding: 20px 0 32px;
      font-size: 0.75rem;
      color: rgba(255,255,255,0.3);
    }
    .footer a {
      color: rgba(100,108,255,0.8);
      text-decoration: none;
    }
  </style>
</head>
<body>
  <div class="wrapper">
    <div class="logo-bar">
      <a href="#" class="logo">
        <span class="logo-icon"></span>
        Grifirio
      </a>
    </div>
    <div class="card">
      <#nested>
    </div>
    <div class="footer">
      <p>Bu e-postayı Grifirio hesabınız için gönderiyoruz.</p>
      <p>© 2026 Grifirio. Tüm hakları saklıdır.</p>
    </div>
  </div>
</body>
</html>
</#macro>
