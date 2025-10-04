"""
Celery Tasks for AI Processing

Bu dosya sadece yapı örneğidir. Gerçek AI implementasyonu ayrı servisler tarafından yapılacak.
"""
from celery import shared_task
from datetime import datetime
import logging

logger = logging.getLogger(__name__)


@shared_task(bind=True, max_retries=3)
def process_graph_data_request(self, message):
    """
    Grafik verisi talebi için Celery task
    
    Bu task:
    1. AI Service 1'e HTTP request gönderir
    2. AI sonucunu bekler
    3. RabbitMQ'ya response mesajı gönderir
    
    Args:
        message: {
            'request_id': str,
            'user_id': str,
            'company_id': str,
            'parameters': dict
        }
    """
    try:
        request_id = message['request_id']
        logger.info(f"Processing graph data request: {request_id}")
        
        # TODO: AI Service 1'e istek gönder
        # ai_result = call_ai_service_1(message)
        
        # Simulated response (gerçek implementasyon AI servisi çağrısı yapacak)
        ai_result = {
            'success': True,
            'data': {
                'graph_type': 'line',
                'values': [10, 20, 30, 40, 50],
                'labels': ['A', 'B', 'C', 'D', 'E']
            }
        }
        
        # Response mesajını RabbitMQ'ya gönder
        from messaging.publishers import rabbitmq_client
        
        response_message = {
            'request_id': request_id,
            'user_id': message['user_id'],
            'success': ai_result['success'],
            'error_message': None,
            'response_time': datetime.utcnow().isoformat(),
            'data': ai_result.get('data')
        }
        
        rabbitmq_client.publish_message(
            exchange='ai.responses',
            routing_key='ai.response.graph',
            message=response_message
        )
        
        logger.info(f"Graph data request {request_id} processed successfully")
        return response_message
        
    except Exception as e:
        logger.error(f"Error processing graph data request: {e}")
        
        # Error response
        error_response = {
            'request_id': message.get('request_id'),
            'user_id': message.get('user_id'),
            'success': False,
            'error_message': str(e),
            'response_time': datetime.utcnow().isoformat(),
            'data': None
        }
        
        from messaging.publishers import rabbitmq_client
        rabbitmq_client.publish_message(
            exchange='ai.responses',
            routing_key='ai.response.graph',
            message=error_response
        )
        
        raise self.retry(exc=e, countdown=60)


@shared_task(bind=True, max_retries=3)
def process_question_request(self, message):
    """
    Soru-cevap talebi için Celery task
    
    Bu task:
    1. AI Service 2'ye HTTP request gönderir
    2. AI sonucunu bekler
    3. RabbitMQ'ya response mesajı gönderir
    
    Args:
        message: {
            'request_id': str,
            'user_id': str,
            'company_id': str,
            'question': str,
            'context': list
        }
    """
    try:
        request_id = message['request_id']
        logger.info(f"Processing question request: {request_id}")
        
        # TODO: AI Service 2'ye istek gönder
        # ai_result = call_ai_service_2(message)
        
        # Simulated response (gerçek implementasyon AI servisi çağrısı yapacak)
        ai_result = {
            'success': True,
            'answer': 'Bu bir örnek AI cevabıdır.',
            'metadata': {
                'confidence': 0.95,
                'processing_time': 1.2
            }
        }
        
        # Response mesajını RabbitMQ'ya gönder
        from messaging.publishers import rabbitmq_client
        
        response_message = {
            'request_id': request_id,
            'user_id': message['user_id'],
            'success': ai_result['success'],
            'error_message': None,
            'response_time': datetime.utcnow().isoformat(),
            'answer': ai_result.get('answer'),
            'metadata': ai_result.get('metadata')
        }
        
        rabbitmq_client.publish_message(
            exchange='ai.responses',
            routing_key='ai.response.question',
            message=response_message
        )
        
        logger.info(f"Question request {request_id} processed successfully")
        return response_message
        
    except Exception as e:
        logger.error(f"Error processing question request: {e}")
        
        # Error response
        error_response = {
            'request_id': message.get('request_id'),
            'user_id': message.get('user_id'),
            'success': False,
            'error_message': str(e),
            'response_time': datetime.utcnow().isoformat(),
            'answer': None,
            'metadata': None
        }
        
        from messaging.publishers import rabbitmq_client
        rabbitmq_client.publish_message(
            exchange='ai.responses',
            routing_key='ai.response.question',
            message=error_response
        )
        
        raise self.retry(exc=e, countdown=60)
