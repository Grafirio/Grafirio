<#import "base.ftl" as layout>
<@layout.emailLayout>
  <h1>E-posta Adresinizi Doğrulayın</h1>
  <p>
    Grifirio'ya hoş geldiniz! Hesabınızı etkinleştirmek için lütfen
    e-posta adresinizi doğrulayın.
  </p>
  <a href="${link}" class="btn">E-postamı Doğrula</a>
  <p style="margin-bottom:4px;font-size:0.8rem">Buton çalışmıyorsa bu bağlantıyı tarayıcınıza kopyalayın:</p>
  <div class="url-fallback">${link}</div>
  <hr class="divider">
  <p style="font-size:0.8rem;margin-bottom:0">Bu talebi siz yapmadıysanız bu e-postayı güvenle görmezden gelebilirsiniz.</p>
</@layout.emailLayout>
