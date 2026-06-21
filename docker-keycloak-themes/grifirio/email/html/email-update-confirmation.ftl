<#import "base.ftl" as layout>
<@layout.emailLayout>
  <h1>E-posta Adresinizi Güncelleyin</h1>
  <p>
    Grifirio hesabınızda e-posta adresinizi güncelleme talebinde bulundunuz.
    Yeni adresinizi doğrulamak için aşağıdaki butona tıklayın.
  </p>
  <a href="${link}" class="btn">E-posta Adresimi Güncelle</a>
  <p style="margin-bottom:4px;font-size:0.8rem">Buton çalışmıyorsa bu bağlantıyı tarayıcınıza kopyalayın:</p>
  <div class="url-fallback">${link}</div>
  <hr class="divider">
  <p style="font-size:0.8rem;margin-bottom:0">Bu talebi siz yapmadıysanız bu e-postayı güvenle görmezden gelebilirsiniz.</p>
</@layout.emailLayout>
