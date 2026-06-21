<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Giriş — Grafirio</title>
  <link rel="stylesheet" href="${url.resourcesPath}/css/grifirio.css">
  <style>
    html,body{margin:0;padding:0;height:100%;}
    .split-layout{display:flex!important;flex-direction:row!important;min-height:100vh!important;width:100%!important;}
    .split-left{flex:0 0 45%!important;display:flex!important;align-items:center!important;justify-content:center!important;padding:2rem 1.5rem!important;background:#1a1a1a!important;}
    .split-right{flex:1 1 0%!important;display:flex!important;align-items:center!important;justify-content:center!important;background:#1a1a1a!important;background-image:radial-gradient(ellipse at 25% 25%,rgba(100,108,255,.4) 0%,transparent 55%),radial-gradient(ellipse at 75% 75%,rgba(83,91,242,.3) 0%,transparent 55%)!important;}
    .form-card{background:#2d2d2d!important;border:1px solid #3a3a3a!important;border-radius:20px!important;box-shadow:0 20px 60px rgba(0,0,0,.5)!important;padding:2.5rem!important;width:100%!important;max-width:420px!important;}
    .brand-logo-wrap{display:inline-block!important;background:#fff!important;border-radius:24px!important;padding:2rem 2.5rem!important;box-shadow:0 12px 50px rgba(0,0,0,.45)!important;margin-bottom:2rem!important;}
    .brand-logo-wrap img{max-width:230px!important;width:100%!important;height:auto!important;display:block!important;}
    @media(max-width:900px){.split-right{display:none!important;}.split-left{flex:1 1 0%!important;}}
  </style>
</head>
<body>

<div class="split-layout">

  <!-- SOL: Form -->
  <div class="split-left">
    <div class="form-card">

      <div class="form-header">
        <h1>Giriş Yap</h1>
        <p>Hesabınıza giriş yapın</p>
      </div>

      <#if message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
        <div class="alert alert-${message.type}">
          ${kcSanitize(message.summary)?no_esc}
        </div>
      </#if>

      <#if realm.password>
        <form id="kc-form-login" action="${url.loginAction}" method="post">

          <#if !usernameHidden??>
            <div class="form-group">
              <label for="username">
                <#if !realm.loginWithEmailAllowed>Kullanıcı Adı
                <#elseif !realm.registrationEmailAsUsername>Kullanıcı Adı veya E-posta
                <#else>E-posta</#if>
              </label>
              <input tabindex="1" id="username" name="username" type="text"
                     value="${(login.username!'')}" autofocus autocomplete="off" />
            </div>
          </#if>

          <div class="form-group">
            <div class="label-row">
              <label for="password">Şifre</label>
              <#if realm.resetPasswordAllowed>
                <a tabindex="5" href="${url.loginResetCredentialsUrl}" class="forgot-link">Şifremi Unuttum</a>
              </#if>
            </div>
            <input tabindex="2" id="password" name="password" type="password" autocomplete="off" />
          </div>

          <#if realm.rememberMe && !usernameHidden??>
            <div class="form-check">
              <input tabindex="3" id="rememberMe" name="rememberMe" type="checkbox"
                     <#if login.rememberMe??>checked</#if> />
              <label for="rememberMe">Beni Hatırla</label>
            </div>
          </#if>

          <input type="hidden" id="id-hidden-input" name="credentialId"
                 <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if> />

          <button tabindex="4" type="submit" class="btn-primary">Giriş Yap</button>

        </form>
      </#if>

      <#if realm.password && social?? && social.providers?has_content>
        <div class="social-divider"><span>veya</span></div>
        <div class="social-list">
          <#list social.providers as p>
            <a href="${p.loginUrl}" class="btn-social">
              <#if p.iconClasses??><i class="${p.iconClasses}"></i></#if>
              ${p.displayName}
            </a>
          </#list>
        </div>
      </#if>

      <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
        <p class="register-link">
          Hesabın yok mu? <a tabindex="6" href="${url.registrationUrl}">Kayıt Ol</a>
        </p>
      </#if>

    </div>
  </div>

  <!-- SAĞ: Branding -->
  <div class="split-right">
    <div class="brand-content">
      <div class="brand-logo-wrap">
        <img src="${url.resourcesPath}/img/logo.png" alt="Grafirio"
             onerror="this.style.display='none';document.getElementById('logo-fallback').style.display='flex'" />
        <div id="logo-fallback" class="brand-logo-fallback" style="display:none">
          <span>G</span>
        </div>
      </div>
      <p class="brand-tagline">Uncover Your Data,<br>Envision the Future.</p>
    </div>
  </div>

</div>
</body>
</html>
