from rest_framework.decorators import api_view
from rest_framework.response import Response
from rest_framework import status
from django.conf import settings
from .serializers import GraphDataRequestSerializer, QuestionRequestSerializer
from messaging.publishers import publish_graph_data_request, publish_question_request
import uuid
from datetime import datetime
import logging

logger = logging.getLogger(__name__)

SYSTEM_PROMPT = """Sen Grifirio adlı bir İş Zekası (BI) platformunun AI asistanısın.
Kullanıcıların veri analizi, istatistik ve iş zeka sorularını yanıtlıyorsun.
Yanıtların kısa, net ve uygulanabilir olsun.
Türkce sorulara Türkce, İngilizce sorulara İngilizce yanıt ver.
"""


@api_view(['POST'])
def request_graph_data(request):
    """
    Grafik verisi talebi endpoint
    Frontend -> Gateway -> Django -> RabbitMQ -> AI Service
    """
    serializer = GraphDataRequestSerializer(data=request.data)
    
    if not serializer.is_valid():
        return Response(serializer.errors, status=status.HTTP_400_BAD_REQUEST)
    
    data = serializer.validated_data
    request_id = data.get('request_id', uuid.uuid4())
    
    # RabbitMQ'ya mesaj gönder
    message = {
        'request_id': str(request_id),
        'user_id': data['user_id'],
        'company_id': data['company_id'],
        'request_time': datetime.utcnow().isoformat(),
        'parameters': data['parameters']
    }
    
    publish_graph_data_request(message)
    
    return Response({
        'request_id': str(request_id),
        'status': 'processing',
        'message': 'Request sent to AI service. Use WebSocket to receive updates.'
    }, status=status.HTTP_202_ACCEPTED)


@api_view(['POST'])
def request_question_answer(request):
    """
    Soru-cevap talebi endpoint
    Frontend -> Gateway -> Django -> RabbitMQ -> AI Service
    """
    serializer = QuestionRequestSerializer(data=request.data)
    
    if not serializer.is_valid():
        return Response(serializer.errors, status=status.HTTP_400_BAD_REQUEST)
    
    data = serializer.validated_data
    request_id = data.get('request_id', uuid.uuid4())
    
    # RabbitMQ'ya mesaj gönder
    message = {
        'request_id': str(request_id),
        'user_id': data['user_id'],
        'company_id': data['company_id'],
        'request_time': datetime.utcnow().isoformat(),
        'question': data['question'],
        'context': data.get('context', [])
    }
    
    publish_question_request(message)
    
    return Response({
        'request_id': str(request_id),
        'status': 'processing',
        'message': 'Question sent to AI service. Use WebSocket to receive answer.'
    }, status=status.HTTP_202_ACCEPTED)


@api_view(['GET'])
def health_check(request):
    """Health check endpoint"""
    return Response({
        'status': 'healthy',
        'service': 'Grafirio AI Service'
    })


@api_view(['POST'])
def chat_with_ai(request):
    """
    Synchronous Gemini chat endpoint — no RabbitMQ/Celery required.
    Body: { "question": str, "history": [{"role": "user"|"model", "content": str}, ...] }
    Returns: { "success": bool, "answer": str, "model": str }
    """
    question = request.data.get('question', '').strip()
    history = request.data.get('history', [])

    if not question:
        return Response({'success': False, 'error': 'question is required'}, status=status.HTTP_400_BAD_REQUEST)

    api_key = getattr(settings, 'GEMINI_API_KEY', '')
    if not api_key:
        return Response({'success': False, 'error': 'GEMINI_API_KEY not configured'}, status=status.HTTP_503_SERVICE_UNAVAILABLE)

    try:
        import google.generativeai as genai

        genai.configure(api_key=api_key)
        model = genai.GenerativeModel(
            model_name='gemini-2.5-flash',
            system_instruction=SYSTEM_PROMPT,
        )

        # Build conversation history for multi-turn chat
        # Gemini uses role 'user' | 'model'; filter out any trailing model messages
        gemini_history = []
        for msg in history:
            role = msg.get('role', 'user')
            content = msg.get('content', '')
            if role in ('user', 'model') and content:
                gemini_history.append({'role': role, 'parts': [{'text': content}]})

        chat = model.start_chat(history=gemini_history)
        response = chat.send_message(question)

        return Response({
            'success': True,
            'answer': response.text,
            'model': 'gemini-2.5-flash',
        })

    except Exception as exc:
        logger.exception('Gemini chat error: %s', exc)
        return Response({'success': False, 'error': str(exc)}, status=status.HTTP_500_INTERNAL_SERVER_ERROR)
