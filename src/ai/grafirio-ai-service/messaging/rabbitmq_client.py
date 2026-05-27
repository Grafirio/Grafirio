import pika
import json
from django.conf import settings
import logging

logger = logging.getLogger(__name__)


class RabbitMQClient:
    """RabbitMQ connection manager"""
    
    def __init__(self):
        self.connection = None
        self.channel = None
    
    def connect(self):
        """Establish connection to RabbitMQ"""
        credentials = pika.PlainCredentials(
            settings.RABBITMQ_USER, 
            settings.RABBITMQ_PASSWORD
        )
        parameters = pika.ConnectionParameters(
            host=settings.RABBITMQ_HOST,
            port=settings.RABBITMQ_PORT,
            credentials=credentials,
            heartbeat=600,
            blocked_connection_timeout=300
        )
        
        self.connection = pika.BlockingConnection(parameters)
        self.channel = self.connection.channel()
        
        # Declare exchanges (durable for persistence)
        self.channel.exchange_declare(
            exchange='ai.requests',
            exchange_type='fanout',
            durable=True
        )
        
        self.channel.exchange_declare(
            exchange='ai.responses',
            exchange_type='topic',
            durable=True
        )
        
        logger.info("Connected to RabbitMQ successfully")
    
    def close(self):
        """Close RabbitMQ connection"""
        if self.connection and not self.connection.is_closed:
            self.connection.close()
            logger.info("RabbitMQ connection closed")
    
    def publish_message(self, exchange, routing_key, message):
        """Publish a message to RabbitMQ"""
        if not self.channel or self.channel.is_closed:
            self.connect()
        
        self.channel.basic_publish(
            exchange=exchange,
            routing_key=routing_key,
            body=json.dumps(message),
            properties=pika.BasicProperties(
                delivery_mode=2,  # make message persistent
                content_type='application/json'
            )
        )
        
        logger.info(f"Published message to {exchange}.{routing_key}")


# Global client instance
rabbitmq_client = RabbitMQClient()
