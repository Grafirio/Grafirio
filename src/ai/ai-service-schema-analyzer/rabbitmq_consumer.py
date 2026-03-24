import pika
import json
import logging
import os
from dotenv import load_dotenv
from schema_analyzer import SchemaAnalyzer
import requests
from datetime import datetime

load_dotenv()

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


class SchemaAnalysisConsumer:
    def __init__(self):
        self.rabbitmq_host = os.getenv('RABBITMQ_HOST', 'localhost')
        self.rabbitmq_port = int(os.getenv('RABBITMQ_PORT', 5672))
        self.rabbitmq_user = os.getenv('RABBITMQ_USER', 'guest')
        self.rabbitmq_password = os.getenv('RABBITMQ_PASSWORD', 'guest123')
        self.backend_api_url = os.getenv('BACKEND_API_URL', 'http://data-analysis.api.container:5221')
        
        self.connection = None
        self.channel = None
    
    def connect(self):
        """Connect to RabbitMQ"""
        try:
            credentials = pika.PlainCredentials(self.rabbitmq_user, self.rabbitmq_password)
            parameters = pika.ConnectionParameters(
                host=self.rabbitmq_host,
                port=self.rabbitmq_port,
                credentials=credentials,
                heartbeat=600,
                blocked_connection_timeout=300
            )
            
            self.connection = pika.BlockingConnection(parameters)
            self.channel = self.connection.channel()
            
            # Declare queue
            self.channel.queue_declare(queue='schema_analysis_queue', durable=True)
            
            logger.info(f"✅ Connected to RabbitMQ: {self.rabbitmq_host}:{self.rabbitmq_port}")
            
        except Exception as e:
            logger.error(f"❌ Failed to connect to RabbitMQ: {e}")
            raise
    
    def send_progress(self, request_id: str, progress: int, message: str):
        """Send progress update to backend API"""
        try:
            url = f"{self.backend_api_url}/api/ai/update-progress"
            payload = {
                "requestId": request_id,
                "progress": progress,
                "message": message,
                "timestamp": datetime.utcnow().isoformat()
            }
            
            response = requests.post(url, json=payload, timeout=5)
            
            if response.ok:
                logger.info(f"📊 Progress sent: {progress}% - {message}")
            else:
                logger.warning(f"⚠️ Progress update failed: {response.status_code}")
                
        except Exception as e:
            logger.error(f"❌ Error sending progress: {e}")
    
    def send_result(self, request_id: str, result: dict, status: str = "completed"):
        """Send analysis result to backend API"""
        try:
            url = f"{self.backend_api_url}/api/ai/analysis-result"
            payload = {
                "requestId": request_id,
                "status": status,
                "result": result,
                "completedAt": datetime.utcnow().isoformat()
            }
            
            response = requests.post(url, json=payload, timeout=10)
            
            if response.ok:
                logger.info(f"✅ Result sent successfully for {request_id}")
            else:
                logger.error(f"❌ Failed to send result: {response.status_code} - {response.text}")
                
        except Exception as e:
            logger.error(f"❌ Error sending result: {e}")
    
    def process_message(self, ch, method, properties, body):
        """Process incoming schema analysis request"""
        try:
            message = json.loads(body)
            logger.info(f"📥 Received analysis request: {message.get('requestId')}")
            
            request_id = message.get('requestId')
            database = message.get('database')
            host = message.get('host')
            port = message.get('port', 1433)
            username = message.get('username', 'sa')
            password = message.get('password')
            tables = message.get('tables', [])
            
            # Step 1: Connection setup (10%)
            self.send_progress(request_id, 10, "Veritabanına bağlanılıyor...")
            
            # Build connection string
            connection_string = (
                f"mssql+pymssql://{username}:{password}"
                f"@{host}:{port}/{database}"
            )
            
            # Step 2: Initialize analyzer (20%)
            self.send_progress(request_id, 20, "Schema analyzer başlatılıyor...")
            analyzer = SchemaAnalyzer(connection_string)
            
            # Step 3: Analyze database (30-70%)
            self.send_progress(request_id, 30, "Tablolar analiz ediliyor...")
            result = analyzer.analyze_database(database)
            
            self.send_progress(request_id, 70, "Gemini AI ile schema analiz ediliyor...")
            
            # Step 4: Save result (90%)
            self.send_progress(request_id, 90, "Sonuçlar kaydediliyor...")
            
            # Add metadata
            result['metadata'] = {
                'database': database,
                'host': host,
                'tables': tables,
                'analyzed_at': datetime.utcnow().isoformat()
            }
            
            # Step 5: Send result to backend (100%)
            self.send_result(request_id, result, "completed")
            self.send_progress(request_id, 100, "Analiz tamamlandı!")
            
            logger.info(f"✅ Analysis completed: {request_id}")
            
            # Acknowledge message
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
        except Exception as e:
            logger.error(f"❌ Error processing message: {e}")
            
            # Send error status
            try:
                request_id = json.loads(body).get('requestId')
                self.send_result(request_id, {"error": str(e)}, "failed")
            except:
                pass
            
            # Reject and don't requeue (dead letter handling recommended)
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    
    def start_consuming(self):
        """Start consuming messages from queue"""
        try:
            self.channel.basic_qos(prefetch_count=1)
            self.channel.basic_consume(
                queue='schema_analysis_queue',
                on_message_callback=self.process_message
            )
            
            logger.info("🚀 Schema Analysis Consumer started. Waiting for messages...")
            logger.info("📡 Press CTRL+C to exit")
            
            self.channel.start_consuming()
            
        except KeyboardInterrupt:
            logger.info("🛑 Shutting down consumer...")
            self.stop()
        except Exception as e:
            logger.error(f"❌ Consumer error: {e}")
            raise
    
    def stop(self):
        """Stop consuming and close connection"""
        if self.channel and self.channel.is_open:
            self.channel.stop_consuming()
        
        if self.connection and self.connection.is_open:
            self.connection.close()
        
        logger.info("👋 Consumer stopped")


def main():
    """Main entry point"""
    consumer = SchemaAnalysisConsumer()
    consumer.connect()
    consumer.start_consuming()


if __name__ == "__main__":
    main()
