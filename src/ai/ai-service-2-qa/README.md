# AI Service 2: Soru-Cevap & Veri Yorumlama Servisi

Bu klasör, soru-cevap ve veri yorumlama yapacak AI servisinin yapısını içerir.

## Yapılacaklar
- [ ] LLM model entegrasyonu (GPT, LLaMA, vb.)
- [ ] RAG (Retrieval-Augmented Generation) implementasyonu
- [ ] Context management
- [ ] REST API endpoint'leri
- [ ] Docker container yapılandırması

## Önerilen Teknolojiler
- FastAPI (Python web framework)
- LangChain / LlamaIndex
- OpenAI API / Local LLM
- Vector database (Pinecone, Weaviate, ChromaDB)
- Docker

## API Endpoint Örneği
```
POST /answer/question
{
    "question": "Geçen ayki satışlar nasıldı?",
    "context": [...]
}

Response:
{
    "success": true,
    "answer": "Geçen ay satışlarınız...",
    "metadata": {
        "confidence": 0.95,
        "sources": [...]
    }
}
```
