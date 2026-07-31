<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

  <#if section = "title">
    Hata
  <#elseif section = "header">
    <p class="gf-eyebrow">Bir şeyler ters gitti</p>
    <h1 class="gf-title">Bu adım tamamlanamadı.</h1>

  <#elseif section = "form">
    <p class="gf-sub">
      <#if message?has_content>${kcSanitize(message.summary)?no_esc}<#else>Beklenmeyen bir hata oluştu.</#if>
    </p>

    <#if client?? && client.baseUrl?has_content>
      <a class="gf-btn" href="${client.baseUrl}">Uygulamaya dön</a>
    <#else>
      <a class="gf-btn" href="https://grafirio.com">Grafirio’ya dön</a>
    </#if>
  </#if>

</@layout.registrationLayout>
