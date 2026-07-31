<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.exists('firstName','lastName','email','username','password','password-confirm'); section>

  <#if section = "title">
    Kayıt
  <#elseif section = "header">
    <p class="gf-eyebrow">Hesap oluştur</p>
    <h1 class="gf-title">İlk dashboard’un 90 saniye uzakta.</h1>
    <p class="gf-sub">Önce seni tanıyalım. Hesabın açıldıktan sonra çalışma alanını kuracağız.</p>

  <#elseif section = "form">

    <form id="kc-register-form" class="gf-form" action="${url.registrationAction}" method="post">

      <div class="gf-field">
        <label class="gf-label" for="firstName">Ad</label>
        <input id="firstName" name="firstName" class="gf-input" type="text"
               value="${(register.formData.firstName!'')}" placeholder="Elif"
               autocomplete="given-name" autofocus
               aria-invalid="<#if messagesPerField.existsError('firstName')>true</#if>"/>
        <#if messagesPerField.existsError('firstName')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('firstName'))?no_esc}</span>
        </#if>
      </div>

      <div class="gf-field">
        <label class="gf-label" for="lastName">Soyad</label>
        <input id="lastName" name="lastName" class="gf-input" type="text"
               value="${(register.formData.lastName!'')}" placeholder="Yılmaz"
               autocomplete="family-name"
               aria-invalid="<#if messagesPerField.existsError('lastName')>true</#if>"/>
        <#if messagesPerField.existsError('lastName')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('lastName'))?no_esc}</span>
        </#if>
      </div>

      <div class="gf-field">
        <label class="gf-label" for="email">İş e-postası</label>
        <input id="email" name="email" class="gf-input" type="email"
               value="${(register.formData.email!'')}" placeholder="ad.soyad@sirket.com"
               autocomplete="email"
               aria-invalid="<#if messagesPerField.existsError('email')>true</#if>"/>
        <#if messagesPerField.existsError('email')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('email'))?no_esc}</span>
        </#if>
      </div>

      <#if !realm.registrationEmailAsUsername>
        <div class="gf-field">
          <label class="gf-label" for="username">Kullanıcı adı</label>
          <input id="username" name="username" class="gf-input" type="text"
                 value="${(register.formData.username!'')}" autocomplete="username"
                 aria-invalid="<#if messagesPerField.existsError('username')>true</#if>"/>
          <#if messagesPerField.existsError('username')>
            <span class="gf-field-error">${kcSanitize(messagesPerField.get('username'))?no_esc}</span>
          </#if>
        </div>
      </#if>

      <#if passwordRequired??>
        <div class="gf-field">
          <span class="gf-label">
            <label for="password">Şifre</label>
            <span class="gf-label-hint">En az 8 karakter</span>
          </span>
          <span class="gf-input-wrap">
            <input id="password" name="password" class="gf-input" type="password"
                   placeholder="En az 8 karakter" autocomplete="new-password"
                   aria-invalid="<#if messagesPerField.existsError('password','password-confirm')>true</#if>"/>
            <button type="button" class="gf-reveal" tabindex="-1"
                    onclick="var p=document.getElementById('password');var s=p.type==='password';p.type=s?'text':'password';this.textContent=s?'Gizle':'Göster';">
              Göster
            </button>
          </span>
          <#if messagesPerField.existsError('password')>
            <span class="gf-field-error">${kcSanitize(messagesPerField.get('password'))?no_esc}</span>
          </#if>
        </div>

        <div class="gf-field">
          <label class="gf-label" for="password-confirm">Şifre tekrar</label>
          <input id="password-confirm" name="password-confirm" class="gf-input" type="password"
                 placeholder="••••••••" autocomplete="new-password"
                 aria-invalid="<#if messagesPerField.existsError('password-confirm')>true</#if>"/>
          <#if messagesPerField.existsError('password-confirm')>
            <span class="gf-field-error">${kcSanitize(messagesPerField.get('password-confirm'))?no_esc}</span>
          </#if>
        </div>
      </#if>

      <#if recaptchaRequired??>
        <div class="g-recaptcha" data-size="compact" data-sitekey="${recaptchaSiteKey}"></div>
      </#if>

      <button class="gf-btn" type="submit">
        Hesabı oluştur <span aria-hidden="true">&rarr;</span>
      </button>
    </form>

    <p class="gf-legal">
      Devam ederek <a href="https://grafirio.com">kullanım koşullarını</a> ve
      <a href="https://grafirio.com">gizlilik politikasını</a> kabul etmiş olursun.
    </p>

    <p class="gf-foot-note">
      <span>Zaten hesabın var mı?</span>
      <a href="${url.loginUrl}">Giriş yap</a>
    </p>

  </#if>

</@layout.registrationLayout>
