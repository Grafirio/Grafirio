// Bu dosya artÄ±k kullanÄ±lmÄ±yor â€” bridge anti-pattern kaldÄ±rÄ±ldÄ±.
// IQuestionRequest mesajlarÄ± MassTransit topoloji konfigÃ¼rasyonu sayesinde
// doÄŸrudan 'ai.requests' (fanout) exchange'ine yayÄ±nlanÄ±r.
// Django AI servisi bu exchange'e baÄŸlÄ± 'django.ai.requests' kuyruÄŸunu dinler.
// BakÄ±nÄ±z: Program.cs â†’ AddGrafirioMassTransit configureTopology callback

namespace Grafirio.DataAnalysis.Api.Features.AI;

