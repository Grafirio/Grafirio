<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('totp'); section>

  <#if section = "title">
    Doğrulama
  <#elseif section = "header">
    <p class="gf-eyebrow">2. adım · doğrulama</p>
    <h1 class="gf-title">Kodu gir, kanvası aç.</h1>
    <p class="gf-sub">Doğrulama uygulamandaki 6 haneli kodu yaz. Kod her 30 saniyede yenilenir.</p>

  <#elseif section = "form">

    <form id="kc-otp-login-form" class="gf-form" action="${url.loginAction}" method="post">

      <#if otpLogin.userOtpCredentials?size gt 1>
        <div class="gf-field">
          <label class="gf-label">Doğrulama yöntemi</label>
          <#list otpLogin.userOtpCredentials as credential>
            <label class="gf-check">
              <input type="radio" name="selectedCredentialId" value="${credential.id}"
                     <#if credential.id == otpLogin.selectedCredentialId>checked</#if>/>
              <span>${credential.userLabel}</span>
            </label>
          </#list>
        </div>
      </#if>

      <div class="gf-field">
        <label class="gf-label" for="otp">Doğrulama kodu</label>
        <input id="otp" name="otp" class="gf-otp" type="text" inputmode="numeric"
               maxlength="6" placeholder="000000" autocomplete="one-time-code" autofocus
               aria-invalid="<#if messagesPerField.existsError('totp')>true</#if>"/>
        <#if messagesPerField.existsError('totp')>
          <span class="gf-field-error">${kcSanitize(messagesPerField.get('totp'))?no_esc}</span>
        </#if>
      </div>

      <button class="gf-btn" type="submit">
        Doğrula ve gir <span aria-hidden="true">&rarr;</span>
      </button>
    </form>

  </#if>

</@layout.registrationLayout>
