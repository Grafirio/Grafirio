from rest_framework.decorators import api_view
from rest_framework.response import Response
from rest_framework import status
from django.conf import settings
from .serializers import GraphDataRequestSerializer, QuestionRequestSerializer
from messaging.publishers import publish_graph_data_request, publish_question_request
import uuid
from datetime import datetime


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
