<#import "template.ftl" as layout>
<@layout.registrationLayout; section>
    <#if section = "header">
        <div class="kc-logo-text">
            <img src="${url.resourcesPath}/img/logo.png" alt="Grafirio" />
        </div>
    <#elseif section = "form">
        <div id="kc-form">
            <div class="error-container">
                <div class="error-icon">⚠</div>
                <h2 class="error-title">${msg("errorTitle")!"Bir hata oluştu"}</h2>
                <p class="error-message">
                    <#if message?has_content>${message.summary?no_esc}</#if>
                </p>
                <#if client?? && client.baseUrl?has_content>
                    <a href="${client.baseUrl}" class="btn-back">
                        ← Uygulamaya geri dön
                    </a>
                </#if>
            </div>
        </div>
    </#if>
</@layout.registrationLayout>
