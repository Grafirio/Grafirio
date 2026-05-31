import pika
import json
import logging
import threading
from django.conf import settings
from channels.layers import get_channel_layer
from asgiref.sync import async_to_sync

logger = logging.getLogger(__name__)
channel_layer = get_channel_layer()


class AIRequestConsumer:
    """
    RabbitMQ consumer for AI requests
    Listens to: ai.requests exchange
    Triggers Celery tasks for AI processing
    """
    
    def __init__(self):
        self.connection = None
        self.channel = None
    
    def connect(self):
        """Establish connection and setup queue"""
        credentials = pika.PlainCredentials(
            settings.RABBITMQ_USER,
            settings.RABBITMQ_PASSWORD
        )
        parameters = pika.ConnectionParameters(
            host=settings.RABBITMQ_HOST,
            port=settings.RABBITMQ_PORT,
            credentials=credentials,
            heartbeat=600
        )
        
        self.connection = pika.BlockingConnection(parameters)
        self.channel = self.connection.channel()

        # Fanout exchange — MassTransit IQuestionRequest buraya yayınlar.
        # Routing key fanout'ta görmezden gelinir; tüm bağlı kuyruklar mesajı alır.
        self.channel.exchange_declare(
            exchange='ai.requests',
            exchange_type='fanout',
            durable=True
        )

        # Declare queue
        self.channel.queue_declare(
            queue='django.ai.requests',
            durable=True
        )
        
        # Bind queue to exchange with routing keys
        self.channel.queue_bind(
            exchange='ai.requests',
            queue='django.ai.requests',
            routing_key='ai.request.graph'
        )
        
        self.channel.queue_bind(
            exchange='ai.requests',
            queue='django.ai.requests',
            routing_key='ai.request.question'
        )
        
        logger.info("AI Request Consumer connected to RabbitMQ")
    
    def _extract_message(self, raw: dict) -> tuple[dict, str]:
        """
        MassTransit envelope'u veya düz mesajı normalize eder.
        Döndürür: (message_dict, source_type)
        MassTransit envelope formatı:
        {
            "messageType": ["urn:message:...:IQuestionRequest"],
            "message": { "requestId": ..., "question": ..., "context": [...] }
        }
        """
        if 'messageType' in raw and 'message' in raw:
            # MassTransit envelope — camelCase → snake_case dönüşümü
            inner = raw['message']
            message = {
                'request_id':   str(inner.get('requestId', '')),
                'user_id':      inner.get('userId', 'web-user'),
                'company_id':   inner.get('companyId', 'default'),
                'request_time': inner.get('requestTime', ''),
                'question':     inner.get('question', ''),
                'context':      inner.get('context', []),  # List[{role, content}]
                'database':     inner.get('database') or '',
                'tables':       inner.get('tables') or [],
                'table_name':   inner.get('tableName') or '',
                'predict_data': inner.get('predictData') or {},
            }
            # Mesaj tipini belirle
            msg_types = raw.get('messageType', [])
            if any('IQuestionRequest' in t for t in msg_types):
                return message, 'ai.request.question'
            if any('IGraphDataRequest' in t for t in msg_types):
                return message, 'ai.request.graph'
            return message, 'unknown'
        else:
            # Düz mesaj (eski format / raw pika publish)
            routing_key = raw.pop('__routing_key__', 'unknown')
            return raw, routing_key

    def callback(self, ch, method, properties, body):
        """MassTransit veya düz RabbitMQ mesajlarını işle."""
        try:
            raw = json.loads(body)

            # MassTransit envelope için routing key'i mesaj tipinden çıkar,
            # düz mesajlar için method.routing_key kullan
            if 'messageType' in raw:
                message, source = self._extract_message(raw)
            else:
                message = raw
                source = method.routing_key

            logger.info(f"AI isteği alındı: {source} — {message.get('request_id')}")

            from tasks import ai_tasks
            import requests as http_requests
            from django.conf import settings as dj_settings
            import os
            from datetime import datetime

            def run_in_thread(fn, msg):
                try:
                    fn(msg)
                except Exception as ex:
                    logger.error(f"Thread task hatası: {type(ex).__name__}: {ex}")
                    # Hata durumunda C# API'ye error sonucu gönder
                    try:
                        csharp_url = (
                            getattr(dj_settings, 'CSHARP_API_URL', None)
                            or os.getenv('CSHARP_API_URL', 'http://host.docker.internal:5221')
                        )
                        import json as _json
                        err_msg = str(ex)
                        # RATE_LIMIT:<seconds> — ai_tasks'tan gelen özel exception
                        if err_msg.startswith('RATE_LIMIT:'):
                            wait_secs = err_msg.split(':')[1]
                            user_msg = f'AI servisi geçici olarak meşgul (rate limit). ~{wait_secs} saniye sonra tekrar deneyin.'
                        elif 'quota' in err_msg.lower() or 'rate' in err_msg.lower():
                            user_msg = 'AI servisi geçici olarak meşgul (rate limit). Lütfen 1 dakika sonra tekrar deneyin.'
                        else:
                            user_msg = f'İşlem sırasında bir hata oluştu: {err_msg[:120]}'
                        http_requests.post(
                            f"{csharp_url}/api/ai/query-result",
                            json={
                                'requestId': msg.get('request_id', ''),
                                'status': 'error',
                                'result': _json.dumps({'type': 'text', 'success': False, 'answer': user_msg, 'charts': []}),
                                'completedAt': datetime.utcnow().isoformat(),
                            },
                            timeout=5,
                        )
                    except Exception as post_err:
                        logger.error(f"Error sonucu gönderilemedi: {post_err}")

            if source == 'ai.request.graph':
                t = threading.Thread(
                    target=run_in_thread,
                    args=(ai_tasks._process_graph_data, message),
                    daemon=True,
                )
                t.start()
            elif source == 'ai.request.question':
                t = threading.Thread(
                    target=run_in_thread,
                    args=(ai_tasks._process_question, message),
                    daemon=True,
                )
                t.start()
            else:
                logger.warning(f"Bilinmeyen mesaj kaynağı/routing key: {source}")

            ch.basic_ack(delivery_tag=method.delivery_tag)

        except Exception as e:
            import traceback
            logger.error(f"Mesaj işleme hatası: {type(e).__name__}: {e}")
            logger.error(traceback.format_exc())
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    
    def start_consuming(self):
        """Start consuming messages"""
        self.channel.basic_qos(prefetch_count=1)
        self.channel.basic_consume(
            queue='django.ai.requests',
            on_message_callback=self.callback
        )
        
        logger.info("Started consuming AI requests...")
        self.channel.start_consuming()
    
    def stop_consuming(self):
        """Stop consuming and close connection"""
        if self.channel:
            self.channel.stop_consuming()
        if self.connection:
            self.connection.close()
        logger.info("Stopped AI request consumer")


class AIResponseConsumer:
    """
    RabbitMQ consumer for AI responses
    Listens to: ai.responses exchange
    """
    
    def __init__(self):
        self.connection = None
        self.channel = None
    
    def connect(self):
        """Establish connection and setup queue"""
        credentials = pika.PlainCredentials(
            settings.RABBITMQ_USER,
            settings.RABBITMQ_PASSWORD
        )
        parameters = pika.ConnectionParameters(
            host=settings.RABBITMQ_HOST,
            port=settings.RABBITMQ_PORT,
            credentials=credentials,
            heartbeat=600
        )
        
        self.connection = pika.BlockingConnection(parameters)
        self.channel = self.connection.channel()
        
        # Declare exchange
        self.channel.exchange_declare(
            exchange='ai.responses',
            exchange_type='topic',
            durable=True
        )
        
        # Declare queue
        result = self.channel.queue_declare(
            queue='django.ai.responses',
            durable=True
        )
        
        # Bind queue to exchange with routing keys
        self.channel.queue_bind(
            exchange='ai.responses',
            queue='django.ai.responses',
            routing_key='ai.response.graph'
        )
        
        self.channel.queue_bind(
            exchange='ai.responses',
            queue='django.ai.responses',
            routing_key='ai.response.question'
        )
        
        logger.info("AI Response Consumer connected to RabbitMQ")
    
    def callback(self, ch, method, properties, body):
        """Handle incoming AI response messages"""
        try:
            message = json.loads(body)
            logger.info(f"Received AI response: {message.get('request_id')}")
            
            # Extract user_id from message
            user_id = message.get('user_id')
            
            if user_id:
                # Send to WebSocket via Channels
                room_group_name = f'ai_user_{user_id}'
                
                async_to_sync(channel_layer.group_send)(
                    room_group_name,
                    {
                        'type': 'ai_response',
                        'data': message
                    }
                )
                
                logger.info(f"Sent response to WebSocket for user {user_id}")
            
            # Acknowledge message
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
        except Exception as e:
            logger.error(f"Error processing AI response: {e}")
            # Reject and requeue on error
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)
    
    def start_consuming(self):
        """Start consuming messages"""
        self.channel.basic_qos(prefetch_count=1)
        self.channel.basic_consume(
            queue='django.ai.responses',
            on_message_callback=self.callback
        )
        
        logger.info("Started consuming AI responses...")
        self.channel.start_consuming()
    
    def stop_consuming(self):
        """Stop consuming and close connection"""
        if self.channel:
            self.channel.stop_consuming()
        if self.connection:
            self.connection.close()
        logger.info("Stopped AI response consumer")


def start_request_consumer():
    """Entry point for starting the request consumer"""
    consumer = AIRequestConsumer()
    consumer.connect()
    consumer.start_consuming()


def start_response_consumer():
    """Entry point for starting the response consumer"""
    consumer = AIResponseConsumer()
    consumer.connect()
    consumer.start_consuming()
