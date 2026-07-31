<#import "template.ftl" as layout>
<@layout.registrationLayout displayInfo=true displayMessage=!messagesPerField.existsError('username'); section>

  <#if section = "title">
    Şifre sıfırlama
  <#elseif section = "header">
    <a class="gf-back" href="${url.loginUrl}">&larr; Girişe dön</a>
    <p class="gf-eyebrow">Şifre sıfırlama</p>
    <h1 class="gf-title">Şifreni sıfırlayalım.</h1>
    <p class="gf-sub">E-posta adresini yaz, sıfırlama bağlantısını gönderelim. Bağlantı bir saat geçerli.</p>

  <#elseif section = "form">

    <form id="kc-reset-password-form" class="gf-form" action="${url.loginAction}" method="post">
      <div class="gf-field">
        <label class="gf-label" for="username">
          <#if !realm.loginWithEmailAllowed>Kullanıcı adı
          <#elseif !realm.registrationEmailAsUsername>Kullanıcı adı veya e-posta
          <#else>İş e-postası</#if>
        </label>
        <input id="username" name="username" class="gf-input" type="text"
               value="${(auth.attemptedUsername!'')}" placeholder="ad.soyad@sirket.com"
               autofocus autocomplete="username"
               aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"/>
        <#if messagesPerField.existsError('username')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('username'))?no_esc}</span>
        </#if>
      </div>

      <button class="gf-btn" type="submit">
        Sıfırlama bağlantısı gönder <span aria-hidden="true">&rarr;</span>
      </button>
    </form>

  <#elseif section = "info">
    <p class="gf-legal">
      Hesap varsa bağlantı birkaç dakika içinde gelir. Gelmezse gereksiz klasörünü kontrol et.
    </p>
  </#if>

</@layout.registrationLayout>
