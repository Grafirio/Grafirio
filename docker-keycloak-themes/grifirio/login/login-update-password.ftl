<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('password','password-confirm'); section>

  <#if section = "title">
    Yeni şifre
  <#elseif section = "header">
    <p class="gf-eyebrow">Şifre yenileme</p>
    <h1 class="gf-title">Yeni bir şifre belirle.</h1>
    <p class="gf-sub">En az 8 karakter. Daha önce kullanmadığın bir şifre seç.</p>

  <#elseif section = "form">

    <form id="kc-passwd-update-form" class="gf-form" action="${url.loginAction}" method="post">
      <input type="text" id="username" name="username" value="${username!''}"
             autocomplete="username" readonly="readonly" style="display:none"/>
      <input type="password" id="password" name="password" autocomplete="current-password"
             style="display:none"/>

      <div class="gf-field">
        <label class="gf-label" for="password-new">Yeni şifre</label>
        <span class="gf-input-wrap">
          <input id="password-new" name="password-new" class="gf-input" type="password"
                 placeholder="En az 8 karakter" autocomplete="new-password" autofocus
                 aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>"/>
          <button type="button" class="gf-reveal" tabindex="-1"
                  onclick="var p=document.getElementById('password-new');var s=p.type==='password';p.type=s?'text':'password';this.textContent=s?'Gizle':'Göster';">
            Göster
          </button>
        </span>
        <#if messagesPerField.existsError('password')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('password'))?no_esc}</span>
        </#if>
      </div>

      <div class="gf-field">
        <label class="gf-label" for="password-confirm">Yeni şifre tekrar</label>
        <input id="password-confirm" name="password-confirm" class="gf-input" type="password"
               placeholder="••••••••" autocomplete="new-password"
               aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>"/>
        <#if messagesPerField.existsError('password-confirm')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}</span>
        </#if>
      </div>

      <#if isAppInitiatedAction??>
        <label class="gf-check">
          <input type="checkbox" id="logout-sessions" name="logout-sessions" value="on" checked/>
          <span>Diğer cihazlardaki oturumları kapat</span>
        </label>
      </#if>

      <button class="gf-btn" type="submit">
        Şifreyi güncelle <span aria-hidden="true">&rarr;</span>
      </button>

      <#if isAppInitiatedAction??>
        <button class="gf-btn gf-btn--ghost" type="submit" name="cancel-aia" value="true">
          Şimdi değil
        </button>
      </#if>
    </form>

  </#if>

</@layout.registrationLayout>
