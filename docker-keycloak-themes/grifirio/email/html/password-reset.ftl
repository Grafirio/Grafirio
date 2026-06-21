<#import "base.ftl" as layout>
<@layout.emailLayout>
  <h1>Şifrenizi Sıfırlayın</h1>
  <p>
    Grifirio hesabınız için şifre sıfırlama talebinde bulundunuz.
    Aşağıdaki butona tıklayarak yeni şifrenizi belirleyebilirsiniz.
  </p>
  <p>Bu bağlantı <strong style="color:rgba(255,255,255,0.7)">${linkExpirationFormatter(linkExpiration)}</strong> süreyle geçerlidir.</p>
  <a href="${link}" class="btn">Şifremi Sıfırla</a>
  <p style="margin-bottom:4px;font-size:0.8rem">Buton çalışmıyorsa bu bağlantıyı tarayıcınıza kopyalayın:</p>
  <div class="url-fallback">${link}</div>
  <hr class="divider">
  <p style="font-size:0.8rem;margin-bottom:0">Bu talebi siz yapmadıysanız bu e-postayı güvenle görmezden gelebilirsiniz.</p>
</@layout.emailLayout>
