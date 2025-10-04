import pika
import json
import logging
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
        
        # Declare exchange
        self.channel.exchange_declare(
            exchange='ai.requests',
            exchange_type='topic',
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
    
    def callback(self, ch, method, properties, body):
        """Handle incoming AI request messages and trigger Celery tasks"""
        try:
            message = json.loads(body)
            routing_key = method.routing_key
            
            logger.info(f"Received AI request: {routing_key} - {message.get('request_id')}")
            
            # Import tasks here to avoid circular imports
            from tasks.ai_tasks import process_graph_data_request, process_question_request
            
            # Trigger appropriate Celery task based on routing key
            if routing_key == 'ai.request.graph':
                process_graph_data_request.delay(message)
            elif routing_key == 'ai.request.question':
                process_question_request.delay(message)
            else:
                logger.warning(f"Unknown routing key: {routing_key}")
            
            # Acknowledge message
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
        except Exception as e:
            logger.error(f"Error processing AI request: {e}")
            # Reject and requeue on error
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)
    
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
