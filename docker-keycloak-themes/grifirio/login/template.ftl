<#--
  bodyClass ve displayWide: bu tema kullanmiyor ama base temadaki sablonlar
  geciriyor (ornegin login-oauth-grant.ftl — device flow'un onay ekrani).
  FreeMarker tanimlanmamis bir parametreyle cagrilinca makroyu calistirmiyor,
  500 doner ve kullanici "An internal server error has occurred" gorur.
  Bizde karsiligi olmayan parametreleri kabul edip yok saymak, base'den gelen
  her sablonu ayri ayri yazmak zorunda kalmadan calistiriyor.
-->
<#macro registrationLayout displayInfo=false displayMessage=true displayRequiredFields=false showAnotherWayIfPresent=true bodyClass="" displayWide=false>
<!DOCTYPE html>
<#-- locale, realm'de uluslararasilastirma kapaliyken hic tanimlanmiyor; parantez
     olmadan "locale.currentLanguageTag!'tr'" yalnizca eksik alani karsilar,
     eksik degiskeni degil ve sablon derlenmeden patlar. -->
<html lang="${(locale.currentLanguageTag)!'tr'}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex, nofollow">
  <title><#nested "title"> — Grafirio</title>
  <link rel="icon" type="image/svg+xml" href="${url.resourcesPath}/img/favicon.svg">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet"
        href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Manrope:wght@400;500;600;700&family=Poppins:wght@500;600;700;800&display=swap">
  <link rel="stylesheet" href="${url.resourcesPath}/css/grafirio.css">
</head>
<body class="gf-body">

<div class="gf-split">

  <!-- Sol kolon: form -->
  <div class="gf-pane">
    <a class="gf-brand" href="https://grafirio.com">
      <span class="gf-brand-mark"><#include "gmark.ftl"></span>
      <span class="gf-brand-name">GRAFIRIO</span>
    </a>

    <div class="gf-pane-body">
      <div class="gf-form-wrap">

        <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
          <div class="gf-alert gf-alert--${message.type}">
            <span class="gf-alert-dot"></span>
            <span class="gf-alert-text">${kcSanitize(message.summary)?no_esc}</span>
          </div>
        </#if>

        <#nested "header">
        <#nested "form">

        <#if displayInfo>
          <div class="gf-pane-info"><#nested "info"></div>
        </#if>

        <#if auth?has_content && auth.showTryAnotherWayLink() && showAnotherWayIfPresent>
          <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post" class="gf-another-way">
            <input type="hidden" name="tryAnotherWay" value="on"/>
            <button type="submit" class="gf-link-btn">Başka bir yolla doğrula</button>
          </form>
        </#if>

      </div>
    </div>

    <div class="gf-pane-foot">
      <span>© ${.now?string("yyyy")} Grafirio</span>
      <#if realm.internationalizationEnabled && locale?? && locale.supported?size gt 1>
        <span class="gf-locales">
          <#list locale.supported as l>
            <a href="${l.url}" <#if l.languageTag == locale.currentLanguageTag>class="is-active"</#if>>${l.languageTag?upper_case}</a>
          </#list>
        </span>
      </#if>
    </div>
  </div>

  <!-- Sag kolon: urun paneli. Dar ekranda tamamen gizleniyor; formun altina
       yigilmis dekoratif bir blok, mobilde girisi asagi itmekten baska ise
       yaramiyordu. -->
  <aside class="gf-showcase" aria-hidden="true">
    <div class="gf-showcase-inner">
      <p class="gf-showcase-eyebrow">Son oturumundan</p>
      <p class="gf-showcase-lead">Veriyi yükledin. Grafirio okumaya devam etti.</p>

      <div class="gf-mock">
        <div class="gf-mock-chrome">
          <span class="gf-mock-dot" style="background:#E4633C"></span>
          <span class="gf-mock-dot" style="background:#F8C630"></span>
          <span class="gf-mock-dot" style="background:#0E8F8C"></span>
          <span class="gf-mock-file">satis_2026_q2.xlsx · kanvas</span>
        </div>

        <div class="gf-mock-body">
          <div class="gf-mock-note">
            <span class="gf-mock-note-mark"><#include "gmark-light.ftl"></span>
            <div>
              <p class="gf-mock-note-label">Grafirio yorumu</p>
              <p class="gf-mock-note-text">Ege bölgesi cirosu %28 arttı; büyümenin %71’i tek bir ürün grubundan geliyor.</p>
            </div>
          </div>

          <div class="gf-mock-card">
            <p class="gf-mock-card-label">Aylık ciro</p>
            <p class="gf-mock-figure">₺4,82M</p>
            <div class="gf-mock-bars">
              <span style="height:38%"></span>
              <span style="height:54%"></span>
              <span style="height:46%"></span>
              <span style="height:70%;background:#2F5FA8"></span>
              <span style="height:60%"></span>
              <span style="height:88%;background:#0E8F8C"></span>
            </div>
          </div>

          <div class="gf-mock-card">
            <p class="gf-mock-card-label">Kanal dağılımı</p>
            <ul class="gf-mock-legend">
              <li><i style="background:#0E8F8C"></i>Bayi<b>%46</b></li>
              <li><i style="background:#F8C630"></i>E-ticaret<b>%33</b></li>
              <li><i style="background:#8A2E8E"></i>Kurumsal<b>%21</b></li>
            </ul>
          </div>
        </div>
      </div>

      <p class="gf-showcase-foot">Verini keşfet, geleceği gör.</p>
    </div>
  </aside>

</div>
</body>
</html>
</#macro>
