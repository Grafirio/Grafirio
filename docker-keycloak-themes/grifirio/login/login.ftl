<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username','password'); section>

  <#if section = "title">
    Giriş
  <#elseif section = "header">
    <p class="gf-eyebrow">Hesabına giriş</p>
    <h1 class="gf-title">Kanvasın seni bekliyor.</h1>
    <p class="gf-sub">Giriş yap, son çalıştığın dashboard’lar ve Grafirio yorumları kaldığın yerden devam etsin.</p>

  <#elseif section = "form">

    <#if realm.password>
      <form id="kc-form-login" class="gf-form" action="${url.loginAction}" method="post"
            onsubmit="login.disabled = true; return true;">

        <#if !usernameHidden??>
          <div class="gf-field">
            <label class="gf-label" for="username">
              <#if !realm.loginWithEmailAllowed>Kullanıcı adı
              <#elseif !realm.registrationEmailAsUsername>Kullanıcı adı veya e-posta
              <#else>İş e-postası</#if>
            </label>
            <input id="username" name="username" class="gf-input" type="text"
                   value="${(login.username!'')}" placeholder="ad.soyad@sirket.com"
                   autofocus autocomplete="username" tabindex="1"
                   aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"/>
            <#if messagesPerField.existsError('username','password')>
              <span class="gf-field-error" aria-live="polite">
                ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
              </span>
            </#if>
          </div>
        </#if>

        <div class="gf-field">
          <span class="gf-label">
            <label for="password">Şifre</label>
            <#if realm.resetPasswordAllowed>
              <a href="${url.loginResetCredentialsUrl}" tabindex="5">Şifremi unuttum</a>
            </#if>
          </span>
          <span class="gf-input-wrap">
            <input id="password" name="password" class="gf-input" type="password"
                   placeholder="••••••••" autocomplete="current-password" tabindex="2"
                   aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"/>
            <button type="button" class="gf-reveal" tabindex="-1"
                    onclick="var p=document.getElementById('password');var s=p.type==='password';p.type=s?'text':'password';this.textContent=s?'Gizle':'Göster';">
              Göster
            </button>
          </span>
        </div>

        <#if realm.rememberMe && !usernameHidden??>
          <label class="gf-check">
            <input id="rememberMe" name="rememberMe" type="checkbox" tabindex="3"
                   <#if login.rememberMe??>checked</#if>/>
            <span>30 gün boyunca oturumu açık tut</span>
          </label>
        </#if>

        <input type="hidden" id="id-hidden-input" name="credentialId"
               <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>/>

        <button class="gf-btn" name="login" id="kc-login" type="submit" tabindex="4">
          Giriş yap <span aria-hidden="true">&rarr;</span>
        </button>
      </form>
    </#if>

    <#if realm.password && social?? && social.providers?has_content>
      <div class="gf-or"><span>veya</span></div>
      <div class="gf-social">
        <#list social.providers as p>
          <a href="${p.loginUrl}" id="social-${p.alias}">${p.displayName!p.alias}</a>
        </#list>
      </div>
    </#if>

    <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
      <p class="gf-foot-note">
        <span>Hesabın yok mu?</span>
        <a href="${url.registrationUrl}" tabindex="6">Ücretsiz başla</a>
      </p>
    </#if>

  </#if>

</@layout.registrationLayout>
