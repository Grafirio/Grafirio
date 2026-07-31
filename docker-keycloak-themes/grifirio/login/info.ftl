<#import "template.ftl" as layout>
<@layout.registrationLayout displayMessage=false; section>

  <#if section = "title">
    Bilgi
  <#elseif section = "header">
    <p class="gf-eyebrow">Bilgi</p>
    <h1 class="gf-title">
      <#if messageHeader??>${kcSanitize(messageHeader)?no_esc}<#else>Hazır.</#if>
    </h1>

  <#elseif section = "form">
    <p class="gf-sub">
      ${kcSanitize(message.summary)?no_esc}
      <#if requiredActions??><#list requiredActions>: <#items as action>${kcSanitize(msg("requiredAction.${action}"))?no_esc}<#sep>, </#items></#list></#if>
    </p>

    <#if skipLink??>
    <#elseif pageRedirectUri?has_content>
      <a class="gf-btn" href="${pageRedirectUri}">Geri dön</a>
    <#elseif actionUri?has_content>
      <a class="gf-btn" href="${actionUri}">Devam et</a>
    <#elseif client.baseUrl?has_content>
      <a class="gf-btn" href="${client.baseUrl}">Uygulamaya dön</a>
    </#if>
  </#if>

</@layout.registrationLayout>
