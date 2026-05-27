// Bu dosya artık kullanılmıyor — bridge anti-pattern kaldırıldı.
// IQuestionRequest mesajları MassTransit topoloji konfigürasyonu sayesinde
// doğrudan 'ai.requests' (fanout) exchange'ine yayınlanır.
// Django AI servisi bu exchange'e bağlı 'django.ai.requests' kuyruğunu dinler.
// Bakınız: Program.cs → AddGrafiiroMassTransit configureTopology callback

namespace Grafirio.DataAnalysis.Api.Features.AI;

