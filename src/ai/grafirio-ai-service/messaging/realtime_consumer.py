"""
Real-time data processing consumer
Listens to data.realtime exchange for new records
Sends to PyCaret Engine for prediction
Publishes results to WebSocket
"""
import pika
import json
import logging
from django.conf import settings
from channels.layers import get_channel_layer
from asgiref.sync import async_to_sync
import requests

logger = logging.getLogger(__name__)
channel_layer = get_channel_layer()


class RealtimeDataConsumer:
    """
    Consumes new data from RabbitMQ and processes it
    """
    
    def __init__(self):
        self.connection = None
        self.channel = None
        self.pycaret_url = settings.AI_SERVICE_2_URL  # PyCaret Engine URL
    
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
            exchange='data.realtime',
            exchange_type='topic',
            durable=True
        )
        
        # Declare queue
        self.channel.queue_declare(
            queue='django.data.realtime',
            durable=True
        )
        
        # Bind queue
        self.channel.queue_bind(
            exchange='data.realtime',
            queue='django.data.realtime',
            routing_key='data.new'
        )
        
        logger.info("Realtime Data Consumer connected to RabbitMQ")
    
    def callback(self, ch, method, properties, body):
        """Handle incoming new data messages"""
        try:
            message = json.loads(body)
            
            company_id = message['company_id']
            table_name = message['table_name']
            data = message['data']
            
            logger.info(f"Processing new data: {company_id}/{table_name}")
            
            # Call PyCaret Engine for prediction
            try:
                prediction_response = requests.post(
                    f"{self.pycaret_url}/predict",
                    json={
                        'company_id': company_id,
                        'table_name': table_name,
                        'data': data
                    },
                    timeout=10
                )
                
                if prediction_response.status_code == 200:
                    predictions = prediction_response.json()['predictions']
                else:
                    logger.warning(f"Prediction failed: {prediction_response.text}")
                    predictions = None
                    
            except Exception as e:
                logger.error(f"Error calling PyCaret Engine: {str(e)}")
                predictions = None
            
            # Calculate metrics
            metrics = self._calculate_metrics(company_id, table_name, data, predictions)
            
            # Send to WebSocket
            self._send_to_websocket(company_id, metrics)
            
            # Acknowledge message
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
        except Exception as e:
            logger.error(f"Error processing realtime data: {str(e)}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=True)
    
    def _calculate_metrics(self, company_id: str, table_name: str, data: dict, predictions: dict) -> dict:
        """
        Calculate real-time metrics
        """
        from datetime import datetime
        
        # TODO: Implement metric calculation logic
        # For now, return simple structure
        
        metrics = {
            'company_id': company_id,
            'table_name': table_name,
            'new_record': data,
            'predictions': predictions,
            'timestamp': datetime.utcnow().isoformat()
        }
        
        return metrics
    
    def _send_to_websocket(self, company_id: str, metrics: dict):
        """
        Send metrics to WebSocket for real-time frontend update
        """
        room_group_name = f'company_{company_id}'
        
        try:
            async_to_sync(channel_layer.group_send)(
                room_group_name,
                {
                    'type': 'realtime_update',
                    'data': metrics
                }
            )
            
            logger.info(f"Sent realtime update to company {company_id}")
            
        except Exception as e:
            logger.error(f"Error sending to WebSocket: {str(e)}")
    
    def start_consuming(self):
        """Start consuming messages"""
        self.channel.basic_qos(prefetch_count=1)
        self.channel.basic_consume(
            queue='django.data.realtime',
            on_message_callback=self.callback
        )
        
        logger.info("Started consuming realtime data...")
        self.channel.start_consuming()
    
    def stop_consuming(self):
        """Stop consuming and close connection"""
        if self.channel:
            self.channel.stop_consuming()
        if self.connection:
            self.connection.close()
        logger.info("Stopped realtime data consumer")


def start_realtime_consumer():
    """Entry point for starting the consumer"""
    consumer = RealtimeDataConsumer()
    consumer.connect()
    consumer.start_consuming()
